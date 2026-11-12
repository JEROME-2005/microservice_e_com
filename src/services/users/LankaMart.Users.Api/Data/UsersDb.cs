using LankaMart.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Users.Api.Data;

public class UsersDb(DbContextOptions<UsersDb> options) : DbContext(options)
{
    public DbSet<CustomerProfile> Customers => Set<CustomerProfile>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<SellerProfile> Sellers => Set<SellerProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("users");
        modelBuilder.Entity<CustomerProfile>(e =>
        {
            e.ToTable("customer_profiles");
            e.HasKey(x => x.UserId);
            e.HasMany(x => x.Addresses).WithOne().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Address>(e =>
        {
            e.ToTable("addresses");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
        });
        modelBuilder.Entity<SellerProfile>(e =>
        {
            e.ToTable("seller_profiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.Status);
        });

        // Transactional outbox/inbox tables (MassTransit EF): outbox_message = events waiting to be sent, outbox_state = delivery
        // bookkeeping, inbox_state = processed message ids (docs/adr/0020-outbox-inbox.md)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
