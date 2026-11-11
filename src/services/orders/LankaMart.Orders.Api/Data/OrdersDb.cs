using LankaMart.Orders.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Data;

public class OrdersDb(DbContextOptions<OrdersDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> Items => Set<OrderItem>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<OrderEvent> Events => Set<OrderEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema("orders");
        b.HasSequence<long>("order_number_seq").StartsAt(880001);          // LM-2026-880001, 880002, … never repeats, safe under concurrency
        b.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.PaymentExpiresAt });
            e.OwnsOne(x => x.DeliveryAddress);                                         // columns delivery_address_line1 etc. in the same table
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Shipments).WithOne().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(ev => ev.OrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.Ignore(x => x.LineTotalCents);
        });
        b.Entity<Shipment>(e =>
        {
            e.ToTable("shipments");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        });
        b.Entity<OrderEvent>(e =>
        {
            e.ToTable("order_events");
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.OrderId);
        });
    }
}
