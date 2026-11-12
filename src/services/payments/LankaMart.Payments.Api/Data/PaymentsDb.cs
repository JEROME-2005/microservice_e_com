using LankaMart.Payments.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Payments.Api.Data;

public class PaymentsDb(DbContextOptions<PaymentsDb> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();
    public DbSet<WebhookEvent> Webhooks => Set<WebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema("payments");
        b.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasIndex(x => x.CustomerId);
        });
        b.Entity<LedgerTransaction>(e =>
        {
            e.ToTable("ledger_transactions");
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.TransactionId);
        });
        b.Entity<LedgerEntry>().ToTable("ledger_entries", t => t.HasCheckConstraint("ck_entry_one_side",
            "(debit_cents > 0 AND credit_cents = 0) OR (credit_cents > 0 AND debit_cents = 0)"));      // every entry is exactly one of debit/credit
        b.Entity<WebhookEvent>(e =>
        {
            e.ToTable("webhook_events");
            e.HasIndex(x => new { x.Gateway, x.GatewayEventId }).IsUnique();         // dedupe: a re-sent webhook can't be processed twice
        });

        // Transactional outbox/inbox tables (MassTransit EF): outbox_message = events waiting to be sent, outbox_state = delivery
        // bookkeeping, inbox_state = processed message ids (docs/adr/0020-outbox-inbox.md)
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}
