using System.Text.Json;
using FluentValidation;
using LankaMart.Payments.Api.Data;
using LankaMart.Payments.Api.Domain;
using LankaMart.Payments.Api.Gateway;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LankaMart.Payments.Api.Features;

public sealed record PayRequest(string CardNumber, int ExpMonth, int ExpYear, string Cvc, string CardHolder)
{
    public override string ToString() => "PayRequest(redacted)";     // card data must never reach a log line
}

public sealed class PayValidator : AbstractValidator<PayRequest>
{
    public PayValidator(TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        RuleFor(x => x.CardNumber).Must(MockRules.Luhn).WithMessage("Card number is invalid.").WithErrorCode("INVALID_CARD");
        RuleFor(x => x.ExpMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.ExpYear).InclusiveBetween(now.Year, now.Year + 20);
        RuleFor(x => x).Must(x => new DateTime(x.ExpYear, x.ExpMonth, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1) > now)
            .WithMessage("Card has expired.").WithName("Expiry")
            .When(x => x.ExpMonth is >= 1 and <= 12 && x.ExpYear >= now.Year && x.ExpYear <= now.Year + 20);   // guard: never build an invalid DateTime
        RuleFor(x => x.Cvc).NotEmpty().Matches(@"^\d{3,4}$");
        RuleFor(x => x.CardHolder).NotEmpty().Length(2, 80);
    }
}

public static class Db
{
    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed class PaymentService(PaymentsDb db, MockGateway gateway, TimeProvider clock)
{
    public async Task<CreatePaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest r, CancellationToken ct)
    {
        if (r.AmountCents <= 0)
        {
            throw new BusinessRuleException("INVALID_AMOUNT", "Amount must be positive.");
        }
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == r.OrderId, ct);
        if (existing is not null)
        {
            return new(existing.Id, existing.Gateway);                       // idempotent: Orders may retry this call after a timeout
        }

        var now = clock.GetUtcNow();
        var p = new Payment { Id = Guid.CreateVersion7(), OrderId = r.OrderId, CustomerId = r.CustomerId, AmountCents = r.AmountCents, Currency = r.Currency, CreatedAt = now, UpdatedAt = now };
        db.Payments.Add(p);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Db.IsUniqueViolation(ex))                                // two concurrent calls: the unique index on order_id lets exactly one win
        {
            existing = await db.Payments.AsNoTracking().SingleAsync(x => x.OrderId == r.OrderId, ct);
            return new(existing.Id, existing.Gateway);
        }
        return new(p.Id, p.Gateway);
    }

    public async Task PayAsync(Guid paymentId, Guid userId, PayRequest card, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId && x.CustomerId == userId, ct) ?? throw new NotFoundException();   // owner only (IDOR)
        var now = clock.GetUtcNow();
        // ATOMIC CLAIM: Created → Processing. A double-click or a replayed request flips 0 rows and is refused.
        var claimed = await db.Payments.Where(x => x.Id == paymentId && x.Status == PaymentStatus.Created)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PaymentStatus.Processing).SetProperty(x => x.UpdatedAt, now), ct);
        if (claimed == 0)
        {
            var current = await db.Payments.AsNoTracking().Where(x => x.Id == paymentId).Select(x => x.Status).SingleAsync(ct);
            throw current switch
            {
                PaymentStatus.Succeeded or PaymentStatus.RefundRequired => new ConflictException("ALREADY_PAID", "This order is already paid."),
                PaymentStatus.Failed => new ConflictException("PAYMENT_FAILED", "This payment failed. Please place a new order."),
                _ => new ConflictException("PAYMENT_IN_PROGRESS", "Your payment is being processed.")
            };
        }
        await gateway.ChargeAsync(p.Id, p.AmountCents, new CardInput(card.CardNumber, card.ExpMonth, card.ExpYear, card.Cvc, card.CardHolder), CancellationToken.None);   // card lives in memory only
    }
}

public sealed class WebhookProcessor(PaymentsDb db, IPublishEndpoint bus, IOptions<MockOptions> opt, TimeProvider clock, ILogger<WebhookProcessor> log)
{
    public async Task HandleAsync(string rawBody, string signature, CancellationToken ct)
    {
        // 1) AUTHENTICITY: HMAC over the RAW body (not re-serialised JSON — that could differ by a space and break the signature)
        if (!MockSigner.Verify(rawBody, signature, opt.Value.WebhookSecret))
        {
            log.LogWarning("Webhook rejected: bad signature");
            throw new UnauthorizedException("INVALID_SIGNATURE", "Invalid signature");
        }

        MockWebhook? evt;
        try
        {
            evt = JsonSerializer.Deserialize<MockWebhook>(rawBody, MockJson.Options);
        }
        catch (JsonException)
        {
            evt = null;
        }
        if (evt is null)
        {
            throw new BusinessRuleException("BAD_PAYLOAD", "Unreadable payload");
        }

        // 2) IDEMPOTENCY: gateways retry. Seen this event id before? → acknowledge and do nothing.
        if (await db.Webhooks.AnyAsync(w => w.Gateway == "mock" && w.GatewayEventId == evt.EventId, ct))
        {
            return;
        }

        var p = await db.Payments.SingleOrDefaultAsync(x => x.Id == evt.PaymentId, ct);
        if (p is null)
        {
            log.LogWarning("Webhook for unknown payment {PaymentId}", evt.PaymentId);
            return;                                                    // acknowledge: retrying won't make the payment appear
        }

        // 3) INTEGRITY: the amount in the webhook must equal what WE asked to charge
        if (evt.AmountCents != p.AmountCents)
        {
            log.LogError("AMOUNT MISMATCH on payment {PaymentId}: expected {Expected}, webhook says {Got}", p.Id, p.AmountCents, evt.AmountCents);
            throw new BusinessRuleException("AMOUNT_MISMATCH", "Amount mismatch");
        }

        var now = clock.GetUtcNow();
        db.Webhooks.Add(new WebhookEvent { Id = Guid.CreateVersion7(), Gateway = "mock", GatewayEventId = evt.EventId, Type = evt.Type, RawPayload = rawBody, ReceivedAt = now });

        object? toPublish = null;
        if (evt.Type == "charge.succeeded" && p.Status is PaymentStatus.Processing or PaymentStatus.Created)
        {
            p.Status = PaymentStatus.Succeeded;
            p.GatewayRef = evt.GatewayRef;
            p.UpdatedAt = now;
            var tx = new LedgerTransaction
            {
                Id = Guid.CreateVersion7(), PaymentId = p.Id, OrderId = p.OrderId, Type = "CHARGE", AmountCents = p.AmountCents, Currency = p.Currency,
                GatewayRef = evt.GatewayRef, IdempotencyKey = $"charge:{evt.GatewayRef}", CreatedAt = now
            };
            // Double entry: the gateway owes us the money (asset ↑ = DEBIT) and we hold it in escrow for the sellers (liability ↑ = CREDIT)
            tx.Entries.Add(new LedgerEntry { Id = Guid.CreateVersion7(), TransactionId = tx.Id, Account = "gateway_receivable", DebitCents = p.AmountCents, CreatedAt = now });
            tx.Entries.Add(new LedgerEntry { Id = Guid.CreateVersion7(), TransactionId = tx.Id, Account = "escrow", CreditCents = p.AmountCents, CreatedAt = now });
            db.Transactions.Add(tx);
            toPublish = new PaymentCompleted(p.Id, p.OrderId, p.CustomerId, p.AmountCents, p.Currency, evt.GatewayRef, now);
        }
        else if (evt.Type == "charge.failed" && p.Status is PaymentStatus.Processing or PaymentStatus.Created)
        {
            p.Status = PaymentStatus.Failed;
            p.FailureCode = evt.FailureCode ?? "unknown";
            p.UpdatedAt = now;
            toPublish = new PaymentFailed(p.Id, p.OrderId, p.CustomerId, p.FailureCode, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Db.IsUniqueViolation(ex))
        {
            return;        // two identical webhooks raced; the unique index let exactly one through
        }
        if (toPublish is not null)
        {
            await bus.Publish(toPublish, CancellationToken.None);                  // ⚠ dual-write (DB then event) — Phase 2 outbox fixes it
        }
    }
}
