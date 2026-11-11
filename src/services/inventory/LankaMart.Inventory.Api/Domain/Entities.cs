namespace LankaMart.Inventory.Api.Domain;

public class InventoryItem
{
    public Guid VariantId { get; set; }            // PK — one row per variant (single warehouse in Phase 1)
    public Guid SellerId { get; set; }             // learned from VariantCreated → lets us enforce "seller edits only own stock"
    public string Sku { get; set; } = "";
    public int QtyOnHand { get; set; }
    public int QtyReserved { get; set; }
    public int ReorderPoint { get; set; } = 5;
    public int Available => QtyOnHand - QtyReserved;
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ReservationStatus { Reserved, Confirmed, Released }

public class Reservation
{
    public Guid Id { get; set; }                   // == orderId → makes "reserve" naturally idempotent (same order can't reserve twice)
    public ReservationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public List<ReservationLine> Lines { get; set; } = [];
}

public class ReservationLine
{
    public Guid ReservationId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
}

public class StockMovement                         // append-only audit of every manual change
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public int Delta { get; set; }
    public string Reason { get; set; } = "";
    public Guid ActorId { get; set; }
    public DateTimeOffset At { get; set; }
}
