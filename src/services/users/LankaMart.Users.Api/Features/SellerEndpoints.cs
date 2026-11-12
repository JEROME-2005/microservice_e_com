using FluentValidation;
using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Users.Api.Features;

public sealed record KycRequest(string BusinessName, string BusinessType, string? BrNumber, string GoodsCategory, string NicNumber,
                                string AddressLine, string District, string BankName, string BankBranch, string BankAccountName, string BankAccountNumber);
public sealed record RejectRequest(string ReasonCode, string? Note);
public sealed record SuspendRequest(string Reason);

public sealed class KycValidator : AbstractValidator<KycRequest>
{
    public static readonly string[] RejectCodes = ["NIC_MISMATCH", "ADDRESS_PROOF_INVALID", "BANK_MISMATCH", "INCOMPLETE", "OTHER"];

    public KycValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().Length(2, 120);
        RuleFor(x => x.BusinessType).Must(t => t is "individual" or "registered");
        RuleFor(x => x.BrNumber).NotEmpty().When(x => x.BusinessType == "registered").WithMessage("BR number is required for registered businesses.");
        RuleFor(x => x.GoodsCategory).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NicNumber).Matches(@"^(\d{9}[VvXx]|\d{12})$").WithMessage("Enter a valid Sri Lankan NIC (old 9 digits+V/X or new 12 digits).");
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(200);
        RuleFor(x => x.District).Must(d => SriLanka.Districts.Contains(d));
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.BankBranch).NotEmpty().MaximumLength(80);
        RuleFor(x => x.BankAccountName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BankAccountNumber).Matches(@"^\d{6,20}$");
    }
}

public sealed class RejectValidator : AbstractValidator<RejectRequest>
{
    public RejectValidator()
    {
        RuleFor(x => x.ReasonCode).Must(c => KycValidator.RejectCodes.Contains(c));
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class SuspendValidator : AbstractValidator<SuspendRequest>
{
    public SuspendValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);   // a reason is MANDATORY for suspensions (audit requirement)
}

public static class SellerEndpoints
{
    private static string Mask(string? s) => string.IsNullOrEmpty(s) ? "" : new string('•', Math.Max(0, s.Length - 4)) + s[Math.Max(0, s.Length - 4)..];

    public static void MapSellers(this WebApplication app)
    {
        var me = app.MapGroup("/api/v1/sellers").WithTags("Sellers").RequireAuthorization(Policies.Seller);

        me.MapGet("/me", async (HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var s = await db.Sellers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == uid, ct)
                    ?? throw new ConflictException("PROFILE_NOT_READY", "Your seller profile is being set up. Try again in a moment.");
            return Results.Ok(new { s.UserId, status = s.Status.ToString(), s.DisplayName, s.BusinessName, s.KycAttempts, s.RejectionReasonCode, s.RejectionNote, s.SubmittedAt, s.DecidedAt });
        });

        me.MapPost("/kyc", async (KycRequest r, HttpContext c, UsersDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            if (!c.User.IsFullyVerified())
            {
                throw new ForbiddenException("VERIFICATION_REQUIRED", "Verify your email and phone before submitting KYC.");   // System Scenarios §3.1 step 1
            }
            var uid = c.User.UserId();
            var s = await db.Sellers.SingleOrDefaultAsync(x => x.UserId == uid, ct) ?? throw new ConflictException("PROFILE_NOT_READY", "Try again in a moment.");
            var now = clock.GetUtcNow();
            s.SubmitKyc(new KycData(r.BusinessName.Trim(), r.BusinessType, r.BrNumber?.Trim(), r.GoodsCategory.Trim(), r.NicNumber.ToUpperInvariant(),
                                    r.AddressLine.Trim(), r.District, r.BankName.Trim(), r.BankBranch.Trim(), r.BankAccountName.Trim(), r.BankAccountNumber), now);
            await bus.Publish(new SellerKycSubmitted(s.UserId, s.BusinessName!, now), ct);   // outbox row ...
            await db.SaveChangesAsync(ct);                                                    // ... committed with the state change (ONE transaction)
            return Results.Accepted(value: new { status = s.Status.ToString() });
        }).WithValidation<KycRequest>();

        // ───── Admin side: Seller Manager (and Super Admin) ─────
        var admin = app.MapGroup("/api/v1/admin/sellers").WithTags("Admin: Sellers").RequireAuthorization(Policies.SellerAdmin);

        admin.MapGet("", async (UsersDb db, string? status, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            var q = db.Sellers.AsNoTracking().AsQueryable();
            if (Enum.TryParse<SellerStatus>(status, true, out var st))
            {
                q = q.Where(x => x.Status == st);
            }
            var total = await q.LongCountAsync(ct);
            var items = await q.OrderBy(x => x.SubmittedAt ?? x.CreatedAt).Skip((p - 1) * size).Take(size)
                .Select(x => new { x.UserId, status = x.Status.ToString(), x.DisplayName, x.BusinessName, x.District, x.KycAttempts, x.SubmittedAt }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), p, size, total));
        });

        admin.MapGet("/{id:guid}", async (Guid id, UsersDb db, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await db.Sellers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id, ct) ?? throw new NotFoundException();
            log.LogInformation("AUDIT pii_view admin={Admin} seller={Seller}", c.User.UserId(), id);     // PII access is itself audited (System Scenarios §5.3)
            return Results.Ok(new
            {
                s.UserId, status = s.Status.ToString(), s.DisplayName, s.Email, s.Phone, s.BusinessName, s.BusinessType, s.BrNumber, s.GoodsCategory,
                nic = Mask(s.NicNumber), s.AddressLine, s.District, s.BankName, s.BankBranch, s.BankAccountName, bankAccount = Mask(s.BankAccountNumber),
                s.KycAttempts, s.RejectionReasonCode, s.RejectionNote, s.SubmittedAt, s.DecidedAt, s.DecidedBy
            });
        });

        static async Task<SellerProfile> Load(UsersDb db, Guid id, CancellationToken ct) =>
            await db.Sellers.SingleOrDefaultAsync(x => x.UserId == id, ct) ?? throw new NotFoundException();

        admin.MapPost("/{id:guid}/start-review", async (Guid id, UsersDb db, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct);
            s.StartReview(c.User.UserId());
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapPost("/{id:guid}/approve", async (Guid id, UsersDb db, IPublishEndpoint bus, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct);
            var now = clock.GetUtcNow();
            s.Approve(c.User.UserId(), now);
            await bus.Publish(new SellerApproved(s.UserId, s.BusinessName ?? s.DisplayName, now), ct);   // was AFTER SaveChanges
            await db.SaveChangesAsync(ct);                                                               // ONE transaction: state + outbox row
            log.LogInformation("AUDIT seller.approve admin={Admin} seller={Seller}", c.User.UserId(), id);
            return Results.NoContent();
        });

        admin.MapPost("/{id:guid}/reject", async (Guid id, RejectRequest r, UsersDb db, IPublishEndpoint bus, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct);
            var now = clock.GetUtcNow();
            s.Reject(c.User.UserId(), r.ReasonCode, r.Note, now);
            await bus.Publish(new SellerRejected(s.UserId, r.ReasonCode, now), ct);
            await db.SaveChangesAsync(ct);
            log.LogInformation("AUDIT seller.reject admin={Admin} seller={Seller} code={Code}", c.User.UserId(), id, r.ReasonCode);
            return Results.NoContent();
        }).WithValidation<RejectRequest>();

        admin.MapPost("/{id:guid}/suspend", async (Guid id, SuspendRequest r, UsersDb db, IPublishEndpoint bus, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct);
            var now = clock.GetUtcNow();
            s.Suspend(c.User.UserId(), now);
            await bus.Publish(new SellerSuspended(s.UserId, r.Reason, now), ct);
            await db.SaveChangesAsync(ct);
            log.LogInformation("AUDIT seller.suspend admin={Admin} seller={Seller} reason={Reason}", c.User.UserId(), id, r.Reason);
            return Results.NoContent();
        }).WithValidation<SuspendRequest>();

        admin.MapPost("/{id:guid}/reinstate", async (Guid id, UsersDb db, IPublishEndpoint bus, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var s = await Load(db, id, ct);
            var now = clock.GetUtcNow();
            s.Reinstate(c.User.UserId(), now);
            await bus.Publish(new SellerReinstated(s.UserId, now), ct);
            await db.SaveChangesAsync(ct);
            log.LogInformation("AUDIT seller.reinstate admin={Admin} seller={Seller}", c.User.UserId(), id);
            return Results.NoContent();
        });
    }
}
