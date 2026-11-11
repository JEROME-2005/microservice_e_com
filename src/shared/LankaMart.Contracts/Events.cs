namespace LankaMart.Contracts.V1;

// Versioned contracts (Principle #5): you may ADD optional fields; never rename, remove or change the meaning of one.
// A breaking change = a new type in a V2 namespace, published alongside V1 until every consumer has moved.

// ───────────── Identity ─────────────
public sealed record UserRegistered(Guid UserId, string Email, string Phone, string FullName, string Role, string Language, DateTimeOffset OccurredAt);
public sealed record UserContactVerified(Guid UserId, string Channel /* "email" | "phone" */, DateTimeOffset OccurredAt);
public sealed record UserLoginFailed(Guid? UserId, string Email, string? Ip, DateTimeOffset OccurredAt);   // Security service consumes this in Phase 6

// ───────────── Users ─────────────
public sealed record SellerKycSubmitted(Guid SellerId, string BusinessName, DateTimeOffset OccurredAt);
public sealed record SellerApproved(Guid SellerId, string BusinessName, DateTimeOffset OccurredAt);
public sealed record SellerRejected(Guid SellerId, string ReasonCode, DateTimeOffset OccurredAt);
public sealed record SellerSuspended(Guid SellerId, string Reason, DateTimeOffset OccurredAt);
public sealed record SellerReinstated(Guid SellerId, DateTimeOffset OccurredAt);

// ───────────── Catalog ─────────────
public sealed record VariantCreated(Guid VariantId, Guid ProductId, Guid SellerId, string Sku, DateTimeOffset OccurredAt);
public sealed record ProductStatusChanged(Guid ProductId, Guid SellerId, string OldStatus, string NewStatus, DateTimeOffset OccurredAt);

// ───────────── Inventory ─────────────
public sealed record InventoryLowStock(Guid VariantId, Guid SellerId, int Available, int ReorderPoint, DateTimeOffset OccurredAt);
public sealed record ReservationConfirmFailed(Guid OrderId, string Reason, DateTimeOffset OccurredAt);

// ───────────── Orders ─────────────
public sealed record OrderLine(Guid VariantId, Guid SellerId, int Quantity, long UnitPriceCents);
public sealed record OrderCreated(Guid OrderId, string OrderNumber, Guid CustomerId, long TotalCents, DateTimeOffset OccurredAt);
public sealed record OrderPaid(Guid OrderId, string OrderNumber, Guid CustomerId, long TotalCents, IReadOnlyList<OrderLine> Lines, DateTimeOffset OccurredAt);
public sealed record OrderCancelled(Guid OrderId, Guid CustomerId, string Reason, IReadOnlyList<OrderLine> Lines, DateTimeOffset OccurredAt);
public sealed record OrderStatusChanged(Guid OrderId, Guid CustomerId, string From, string To, DateTimeOffset OccurredAt);
public sealed record OrderPaymentOrphaned(Guid OrderId, Guid PaymentId, DateTimeOffset OccurredAt);   // paid after the order expired/cancelled → needs refund (Phase 5)

// ───────────── Payments ─────────────
public sealed record PaymentCompleted(Guid PaymentId, Guid OrderId, Guid CustomerId, long AmountCents, string Currency, string GatewayRef, DateTimeOffset OccurredAt);
public sealed record PaymentFailed(Guid PaymentId, Guid OrderId, Guid CustomerId, string FailureCode, DateTimeOffset OccurredAt);
