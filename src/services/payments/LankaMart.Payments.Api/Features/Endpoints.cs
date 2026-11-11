using FluentValidation;
using LankaMart.Payments.Api.Data;
using LankaMart.Payments.Api.Domain;
using LankaMart.Payments.Api.Gateway;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Payments.Api.Features;

public sealed class CreateIntentValidator : AbstractValidator<CreatePaymentIntentRequest>
{
    public CreateIntentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Currency).Equal("LKR");     // single currency in Phase 1
    }
}

public static class Endpoints
{
    public static void MapPayments(this WebApplication app)
    {
        // INTERNAL (Orders → Payments)
        app.MapInternalGroup("/api/v1/internal/payments")
           .MapPost("/intent", async (CreatePaymentIntentRequest r, PaymentService s, CancellationToken ct) => Results.Ok(await s.CreateIntentAsync(r, ct)))
           .WithValidation<CreatePaymentIntentRequest>();

        // CUSTOMER
        var g = app.MapGroup("/api/v1/payments").WithTags("Payments").RequireAuthorization();
        g.MapGet("/{id:guid}", async (Guid id, HttpContext c, PaymentsDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == uid, ct) ?? throw new NotFoundException();
            return Results.Ok(new { p.Id, p.OrderId, status = p.Status.ToString(), p.AmountCents, p.Currency, p.Gateway, method = "card_form", p.FailureCode });
        });
        g.MapPost("/{id:guid}/pay", async (Guid id, PayRequest r, HttpContext c, PaymentService s, CancellationToken ct) =>
        {
            await s.PayAsync(id, c.User.UserId(), r, ct);
            return Results.Accepted(value: new { status = "processing" });
        }).WithValidation<PayRequest>();

        // WEBHOOK (public on purpose; protected by the HMAC signature, not by a JWT)
        app.MapPost("/api/v1/webhooks/mock", async (HttpRequest req, WebhookProcessor p, CancellationToken ct) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);                  // RAW body, needed to verify the signature
            await p.HandleAsync(body, req.Headers["X-Mock-Signature"].ToString(), ct);
            return Results.Ok();                                         // fast 200 so the gateway stops retrying
        }).AllowAnonymous().WithTags("Webhooks");

        // FAILURE LAB (dev only) — System Scenarios §7: "test your saga compensations against every one of these weekly"
        if (app.Environment.IsDevelopment())
        {
            var lab = app.MapGroup("/api/v1/dev/mock").WithTags("Dev: failure lab").AllowAnonymous();
            lab.MapPost("/{paymentId:guid}/duplicate-webhook", async (Guid paymentId, MockWebhookSender s) =>     // SAME event id → must be a harmless no-op
                MockWebhookSender.LastSent.TryGetValue(paymentId, out var last) ? Results.Ok(new { httpStatus = await s.SendAsync(last) }) : Results.NotFound());
            lab.MapPost("/{paymentId:guid}/bad-signature", async (Guid paymentId, MockWebhookSender s) =>          // must be 401
                MockWebhookSender.LastSent.TryGetValue(paymentId, out var last)
                    ? Results.Ok(new { httpStatus = await s.SendAsync(last with { EventId = Guid.NewGuid() }, validSignature: false, remember: false) })
                    : Results.NotFound());
            lab.MapPost("/{paymentId:guid}/amount-mismatch", async (Guid paymentId, MockWebhookSender s) =>        // must be 422
                MockWebhookSender.LastSent.TryGetValue(paymentId, out var last)
                    ? Results.Ok(new { httpStatus = await s.SendAsync(last with { EventId = Guid.NewGuid(), AmountCents = 1 }, remember: false) })
                    : Results.NotFound());
        }
    }
}

public sealed class OrderPaymentOrphanedConsumer(PaymentsDb db, ILogger<OrderPaymentOrphanedConsumer> log) : IConsumer<OrderPaymentOrphaned>
{
    public async Task Consume(ConsumeContext<OrderPaymentOrphaned> context)
    {
        // money was taken for an order that no longer exists → flag for the refund flow (Phase 5)
        var n = await db.Payments.Where(p => p.Id == context.Message.PaymentId && p.Status == PaymentStatus.Succeeded)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.RefundRequired));
        if (n > 0)
        {
            log.LogError("Payment {PaymentId} needs a REFUND (order {OrderId} no longer valid)", context.Message.PaymentId, context.Message.OrderId);
        }
    }
}
