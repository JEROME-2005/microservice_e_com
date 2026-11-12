using FluentValidation;
using LankaMart.Identity.Api.Data;
using LankaMart.Identity.Api.Domain;
using LankaMart.Identity.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LankaMart.Identity.Api.Features;

// ── DTOs: exactly the fields a client may send (mass-assignment defence) ──
public sealed record RegisterRequest(string Email, string Phone, string Password, string FullName, string Role = "customer", string Language = "en");
public sealed record LoginRequest(string Email, string Password);
public sealed record VerifyOtpRequest(string Email, string Channel, string Code);
public sealed record ResendOtpRequest(string Email, string Channel);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType = "Bearer");

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalizeLk(p, out _)).WithMessage("Enter a valid Sri Lankan mobile number.");
        RuleFor(x => x.Password).MinimumLength(10).MaximumLength(100);
        RuleFor(x => x.FullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Role).Must(r => r is Roles.Customer or Roles.Seller).WithMessage("Role must be 'customer' or 'seller'.");   // ← NEVER allow admin roles here
        RuleFor(x => x.Language).Must(l => l is "en" or "si" or "ta");
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(100);
    }
}

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Channel).Must(c => c is "email" or "phone");
        RuleFor(x => x.Code).Matches(@"^\d{6}$");
    }
}

public sealed class ResendOtpValidator : AbstractValidator<ResendOtpRequest>
{
    public ResendOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Channel).Must(c => c is "email" or "phone");
    }
}

public sealed class RefreshValidator : AbstractValidator<RefreshRequest>
{
    public RefreshValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class LogoutValidator : AbstractValidator<LogoutRequest>
{
    public LogoutValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

public sealed class AuthService(UserManager<AppUser> users, IdentityDb db, TokenService tokens, OtpService otp,
    IPublishEndpoint bus, IConnectionMultiplexer redis, TimeProvider clock, ILogger<AuthService> log)
{
    public async Task RegisterAsync(RegisterRequest r, CancellationToken ct)
    {
        PhoneNumber.TryNormalizeLk(r.Phone, out var phone);            // validator already proved it parses
        var email = r.Email.Trim().ToLowerInvariant();

        if (await users.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException("EMAIL_TAKEN", "An account with this email already exists.");
        }
        if (await db.Users.AnyAsync(u => u.PhoneNumber == phone, ct))
        {
            throw new ConflictException("PHONE_TAKEN", "An account with this phone number already exists.");
        }

        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, PhoneNumber = phone, FullName = r.FullName.Trim(), Language = r.Language };

        // ONE transaction: user row + role + UserRegistered outbox row commit together, or not at all (UserManager saves internally,
        // so a plain "SaveChanges last" isn't enough — docs/adr/0020-outbox-inbox.md). Any exception below rolls everything back.
        await db.InTransactionAsync(async () =>
        {
            IdentityResult created;
            try
            {
                created = await users.CreateAsync(user, r.Password);   // hashes the password (PBKDF2) — we never see/store it in clear
            }
            catch (DbUpdateException)                                   // two registrations racing for the same phone → unique index is the safety net
            {
                throw new ConflictException("PHONE_TAKEN", "An account with this phone number already exists.");
            }
            if (!created.Succeeded)
            {
                if (created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                {
                    throw new ConflictException("EMAIL_TAKEN", "An account with this email already exists.");
                }
                throw new BusinessRuleException("WEAK_PASSWORD", string.Join(" ", created.Errors.Select(e => e.Description)));
            }
            await users.AddToRoleAsync(user, r.Role);
            await bus.Publish(new UserRegistered(user.Id, email, phone, user.FullName, r.Role, r.Language, clock.GetUtcNow()), ct);   // → outbox row, same transaction
        }, ct);

        // AFTER the commit: Redis and the Notification service are not part of the DB transaction. A user who exists but
        // didn't get a code can simply press "resend".
        await otp.SendAsync(user.Id, "email", email);
        await otp.SendAsync(user.Id, "phone", phone);
    }

    public async Task VerifyContactAsync(VerifyOtpRequest r, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant());
        // same error for "no such user" and "wrong code" → attackers can't use this endpoint to discover which emails are registered
        if (user is null || !await otp.VerifyAsync(user.Id, r.Channel, r.Code))
        {
            throw new BusinessRuleException("INVALID_OTP", "Invalid or expired code.");
        }

        if (r.Channel == "email")
        {
            user.EmailConfirmed = true;
        }
        else
        {
            user.PhoneNumberConfirmed = true;
        }
        await db.InTransactionAsync(async () =>
        {
            await users.UpdateAsync(user);
            await bus.Publish(new UserContactVerified(user.Id, r.Channel, clock.GetUtcNow()), ct);
        }, ct);
    }

    public async Task ResendOtpAsync(ResendOtpRequest r)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant());
        if (user is null)
        {
            return;                                       // always answer 202 → no account enumeration
        }
        await otp.SendAsync(user.Id, r.Channel, r.Channel == "email" ? user.Email! : user.PhoneNumber!);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest r, string? ip, string? ua, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant())
                   ?? throw new UnauthorizedException("INVALID_CREDENTIALS", "Email or password is incorrect.");

        if (await users.IsLockedOutAsync(user))
        {
            throw new TooManyRequestsException("ACCOUNT_LOCKED", "Too many failed attempts. Try again in a few minutes.");
        }

        if (!await users.CheckPasswordAsync(user, r.Password))
        {
            await db.InTransactionAsync(async () =>
            {
                await users.AccessFailedAsync(user);                    // 5 failures → locked 10 minutes (configured in Program.cs)
                await bus.Publish(new UserLoginFailed(user.Id, user.Email!, ip, clock.GetUtcNow()), ct);
            }, ct);
            // throw only AFTER the commit — throwing inside the transaction would roll back the failure counter too
            if (await users.IsLockedOutAsync(user))
            {
                throw new TooManyRequestsException("ACCOUNT_LOCKED", "Too many failed attempts. Try again in a few minutes.");
            }
            throw new UnauthorizedException("INVALID_CREDENTIALS", "Email or password is incorrect.");
        }
        await users.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, Guid.CreateVersion7(), ip, ua, ct);   // new login = new token family
    }

    public async Task<TokenResponse> RefreshAsync(string rawToken, string? ip, string? ua, CancellationToken ct)
    {
        var hash = TokenService.Hash(rawToken);
        var t = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct)
                ?? throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "Please sign in again.");
        var now = clock.GetUtcNow();

        // ATOMIC CLAIM: only one request can flip used_at from NULL → value. Two simultaneous refreshes can't both win.
        var claimed = await db.RefreshTokens
            .Where(x => x.Id == t.Id && x.UsedAt == null && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct);

        if (claimed == 0)
        {
            if (t.UsedAt != null || t.RevokedAt != null)                // an OLD token came back → assume theft → revoke the whole family
            {
                await db.RefreshTokens.Where(x => x.FamilyId == t.FamilyId && x.RevokedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
                log.LogWarning("Refresh-token REUSE detected: user {UserId}, family {FamilyId} revoked", t.UserId, t.FamilyId);
            }
            throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "Please sign in again.");
        }

        var user = await users.FindByIdAsync(t.UserId.ToString());
        if (user is null || await users.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "Please sign in again.");
        }
        return await IssueAsync(user, t.FamilyId, ip, ua, ct);          // same family, brand-new refresh token
    }

    public async Task LogoutAsync(string rawRefreshToken, Guid userId, string? jti, long? exp, CancellationToken ct)
    {
        var hash = TokenService.Hash(rawRefreshToken);
        var t = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        var now = clock.GetUtcNow();
        if (t is not null && t.UserId == userId)                        // you can only revoke your own sessions
        {
            await db.RefreshTokens.Where(x => x.FamilyId == t.FamilyId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        }

        // A JWT can't be "un-signed", so we blacklist its jti until it would have expired anyway. The gateway checks this on every request.
        if (jti is not null && exp is not null)
        {
            var ttl = DateTimeOffset.FromUnixTimeSeconds(exp.Value) - now;
            if (ttl > TimeSpan.Zero)
            {
                await redis.GetDatabase().StringSetAsync($"bl:{jti}", "1", ttl);
            }
        }
    }

    private async Task<TokenResponse> IssueAsync(AppUser user, Guid familyId, string? ip, string? ua, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var access = tokens.CreateAccessToken(user, roles);
        var (raw, entity) = tokens.NewRefreshToken(user.Id, familyId, ip, ua);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return new TokenResponse(access, raw, tokens.AccessTokenSeconds);
    }
}
