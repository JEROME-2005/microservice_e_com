using LankaMart.Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Data;

public class InventoryDb(DbContextOptions<InventoryDb> options) : DbContext(options)
{
    public DbSet<InventoryItem> Items => Set<InventoryItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationLine> ReservationLines => Set<ReservationLine>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema("inventory");
        b.Entity<InventoryItem>(e =>
        {
            e.HasKey(x => x.VariantId);
            e.HasIndex(x => x.SellerId);
            e.Ignore(x => x.Available);
            // DB-level safety net: even a buggy query can never make stock negative or over-reserved
            e.ToTable("inventory_items", t => t.HasCheckConstraint("ck_stock_nonneg", "qty_on_hand >= 0 AND qty_reserved >= 0 AND qty_reserved <= qty_on_hand"));
        });
        b.Entity<Reservation>(e =>
        {
            e.ToTable("reservations");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ReservationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReservationLine>(e =>
        {
            e.ToTable("reservation_lines");
            e.HasKey(x => new { x.ReservationId, x.VariantId });
        });
        b.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements");
            e.HasIndex(x => x.VariantId);
        });

        // Transactional outbox/inbox tables (MassTransit EF): outbox_message = events waiting to be sent, outbox_state = delivery
        // bookkeeping, inbox_state = processed message ids (docs/adr/0020-outbox-inbox.md)
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}
