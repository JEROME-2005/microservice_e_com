namespace LankaMart.Orders.Api.Domain;

public enum OrderStatus
{
    PendingPayment, Paid, Processing, ReadyForPickup, PickedUp, AtHub, OutForDelivery, Delivered, Completed,   // happy path (architecture doc §4.5)
    Cancelled, PaymentFailed, Expired, RefundPending, Refunded                                                 // exits
}

public class Order
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";                 // human friendly: LM-2026-880123
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public long SubtotalCents { get; set; }
    public long DeliveryFeeCents { get; set; }
    public long DiscountCents { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = "LKR";
    public Guid? PaymentId { get; set; }
    public AddressSnapshot DeliveryAddress { get; set; } = new();  // SNAPSHOT: editing the address book later never changes where this order ships
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset PaymentExpiresAt { get; set; }
    public string? CancelReason { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<Shipment> Shipments { get; set; } = [];
    public List<OrderEvent> Events { get; set; } = [];
}

public class AddressSnapshot
{
    public string RecipientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string City { get; set; } = "";
    public string District { get; set; } = "";
    public string? PostalCode { get; set; }
}

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid VariantId { get; set; }                  // plain ids: NO foreign key to another service's tables
    public Guid ProductId { get; set; }
    public Guid SellerId { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? ImageUrlSnapshot { get; set; }
    public int Quantity { get; set; }
    public long UnitPriceCents { get; set; }             // frozen at purchase time
    public long LineTotalCents => UnitPriceCents * Quantity;
}

/// <summary>One order can span several sellers → one shipment per seller; each will later travel independently (pickup → hub → delivery).</summary>
public class Shipment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid SellerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
}

public class OrderEvent   // append-only: the tracking UI is literally a rendering of this table
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string Actor { get; set; } = "system";
    public string? Note { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>PURE logic (no database) → trivially unit-testable. Every transition in the system goes through here.</summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.Paid, OrderStatus.Cancelled, OrderStatus.PaymentFailed, OrderStatus.Expired],
        [OrderStatus.Paid] = [OrderStatus.Processing, OrderStatus.RefundPending, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.ReadyForPickup, OrderStatus.RefundPending, OrderStatus.Cancelled],
        [OrderStatus.ReadyForPickup] = [OrderStatus.PickedUp, OrderStatus.RefundPending],
        [OrderStatus.PickedUp] = [OrderStatus.AtHub],
        [OrderStatus.AtHub] = [OrderStatus.OutForDelivery],
        [OrderStatus.OutForDelivery] = [OrderStatus.Delivered, OrderStatus.AtHub],          // AtHub again = failed delivery attempt
        [OrderStatus.Delivered] = [OrderStatus.Completed, OrderStatus.RefundPending],
        [OrderStatus.RefundPending] = [OrderStatus.Refunded],
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed.TryGetValue(from, out var t) && t.Contains(to);

    public static void EnsureAllowed(OrderStatus from, OrderStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new BusinessRuleException("INVALID_TRANSITION", $"Order cannot move from {from} to {to}.");
        }
    }

    /// <summary>The next step on the happy path (used by the dev simulator).</summary>
    public static OrderStatus? Next(OrderStatus s) => s switch
    {
        OrderStatus.Paid => OrderStatus.Processing,
        OrderStatus.Processing => OrderStatus.ReadyForPickup,
        OrderStatus.ReadyForPickup => OrderStatus.PickedUp,
        OrderStatus.PickedUp => OrderStatus.AtHub,
        OrderStatus.AtHub => OrderStatus.OutForDelivery,
        OrderStatus.OutForDelivery => OrderStatus.Delivered,
        OrderStatus.Delivered => OrderStatus.Completed,
        _ => null
    };
}
