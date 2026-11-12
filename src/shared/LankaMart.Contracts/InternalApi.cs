namespace LankaMart.Contracts.V1.Internal;

// Request/response shapes for SERVICE-TO-SERVICE REST calls (/api/v1/internal/*, never routed by the gateway).

// Catalog: POST /api/v1/internal/variants/lookup
public sealed record VariantLookupRequest(IReadOnlyList<Guid> VariantIds);
public sealed record VariantInfo(Guid VariantId, Guid ProductId, Guid SellerId, string Sku, string ProductName,
                                 string? ImageUrl, long PriceCents, bool IsPurchasable);
public sealed record VariantLookupResponse(IReadOnlyList<VariantInfo> Variants);

// Inventory: POST /api/v1/internal/inventory/reserve | confirm | release
public sealed record StockLine(Guid VariantId, int Quantity);
public sealed record ReserveStockRequest(Guid ReservationId, IReadOnlyList<StockLine> Lines, int TtlSeconds);
public sealed record ReserveStockResponse(bool Success, IReadOnlyList<Guid> UnavailableVariantIds);

// Payments: POST /api/v1/internal/payments/intent
public sealed record CreatePaymentIntentRequest(Guid OrderId, Guid CustomerId, long AmountCents, string Currency);
public sealed record CreatePaymentIntentResponse(Guid PaymentId, string Gateway);

// Notifications: POST /api/v1/internal/notifications/otp — a SYNCHRONOUS call on purpose: an OTP must never sit on the event bus,
// in outbox tables or in broker logs, and the user is waiting for it. Channel: "email" | "phone". The code is never stored.
public sealed record SendOtpRequest(string Channel, string Destination, string Code, string Language)
{
    public override string ToString() => $"SendOtpRequest({Channel}, redacted)";    // never let the code reach a log line
}
