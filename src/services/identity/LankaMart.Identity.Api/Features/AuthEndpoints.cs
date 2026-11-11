using LankaMart.Identity.Api.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace LankaMart.Identity.Api.Features;

public static class AuthEndpoints
{
    // Behind the gateway the TCP peer is the gateway; YARP puts the real client in X-Forwarded-For.
    // ⚠ Spoofable if clients can reach this service directly → in Phase 6 NetworkPolicy makes the gateway the only caller.
    private static string? ClientIp(HttpContext c) =>
        c.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? c.Connection.RemoteIpAddress?.ToString();

    public static void MapAuth(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth");

        g.MapPost("/register", async (RegisterRequest r, AuthService s, CancellationToken ct) =>
        {
            await s.RegisterAsync(r, ct);
            return Results.Accepted(value: new { message = "Account created. Enter the codes we sent to your email and phone." });
        }).AllowAnonymous().WithValidation<RegisterRequest>();

        g.MapPost("/verify-otp", async (VerifyOtpRequest r, AuthService s, CancellationToken ct) =>
        {
            await s.VerifyContactAsync(r, ct);
            return Results.NoContent();
        }).AllowAnonymous().WithValidation<VerifyOtpRequest>();

        g.MapPost("/resend-otp", async (ResendOtpRequest r, AuthService s) =>
        {
            await s.ResendOtpAsync(r);
            return Results.Accepted();
        }).AllowAnonymous().WithValidation<ResendOtpRequest>();

        g.MapPost("/login", async (LoginRequest r, AuthService s, HttpContext c, CancellationToken ct) =>
            Results.Ok(await s.LoginAsync(r, ClientIp(c), c.Request.Headers.UserAgent, ct)))
            .AllowAnonymous().WithValidation<LoginRequest>();

        g.MapPost("/refresh", async (RefreshRequest r, AuthService s, HttpContext c, CancellationToken ct) =>
            Results.Ok(await s.RefreshAsync(r.RefreshToken, ClientIp(c), c.Request.Headers.UserAgent, ct)))
            .AllowAnonymous().WithValidation<RefreshRequest>();

        g.MapPost("/logout", async (LogoutRequest r, AuthService s, HttpContext c, CancellationToken ct) =>
        {
            long? exp = long.TryParse(c.User.FindFirst("exp")?.Value, out var e) ? e : null;
            await s.LogoutAsync(r.RefreshToken, c.User.UserId(), c.User.FindFirst("jti")?.Value, exp, ct);
            return Results.NoContent();
        }).WithValidation<LogoutRequest>();   // requires a valid token (fallback policy)

        g.MapGet("/me", (HttpContext c) => Results.Ok(new
        {
            userId = c.User.UserId(),
            email = c.User.FindFirst("email")?.Value,
            roles = c.User.FindAll("role").Select(x => x.Value),
            emailVerified = c.User.HasFlag("email_verified"),
            phoneVerified = c.User.HasFlag("phone_verified"),
            sellerId = c.User.FindFirst("seller_id")?.Value
        }));
    }

    public static void MapWellKnown(this WebApplication app)
    {
        // Other services (and the gateway) call these to learn how to verify our tokens.
        app.MapGet("/.well-known/openid-configuration", (IOptions<AuthOptions> o) => Results.Ok(new
        {
            issuer = o.Value.Issuer,
            jwks_uri = $"{o.Value.Authority.TrimEnd('/')}/.well-known/jwks.json",
            id_token_signing_alg_values_supported = new[] { "RS256" }
        })).AllowAnonymous();

        app.MapGet("/.well-known/jwks.json", (RsaKeyProvider k) => Results.Ok(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA", use = "sig", alg = "RS256", kid = k.KeyId,
                    n = Base64UrlEncoder.Encode(k.PublicParameters.Modulus), e = Base64UrlEncoder.Encode(k.PublicParameters.Exponent)
                }
            }
        })).AllowAnonymous();
    }

    public static void MapDevEndpoints(this WebApplication app)
    {
        // DEV ONLY (called only when IsDevelopment): lets the web client / seed script read OTPs without a real SMS gateway.
        app.MapGet("/api/v1/dev/otp/{destination}", async (string destination, IConnectionMultiplexer r) =>
        {
            var code = await r.GetDatabase().StringGetAsync($"dev:otp:{destination}");
            return code.HasValue ? Results.Ok(new { destination, code = code.ToString() }) : Results.NotFound();
        }).AllowAnonymous();
    }
}
