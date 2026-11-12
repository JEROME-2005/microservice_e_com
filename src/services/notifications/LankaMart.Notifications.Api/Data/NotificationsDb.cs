using LankaMart.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Data;

public class NotificationsDb(DbContextOptions<NotificationsDb> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<NotificationMessage> Messages => Set<NotificationMessage>();
    public DbSet<InboxItem> Inbox => Set<InboxItem>();
    public DbSet<Preference> Preferences => Set<Preference>();
    public DbSet<DeviceToken> Devices => Set<DeviceToken>();
    public DbSet<Template> Templates => Set<Template>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema("notifications");
        b.Entity<Contact>(e =>
        {
            e.ToTable("contacts");
            e.HasKey(x => x.UserId);
        });
        b.Entity<NotificationMessage>(e =>
        {
            e.ToTable("notification_messages");                       // DeliveryWorker's raw claim SQL uses this exact (schema-qualified) name
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("dedupe_key IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
        b.Entity<InboxItem>(e =>
        {
            e.ToTable("inbox_items");
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
        b.Entity<Preference>(e =>
        {
            e.ToTable("preferences");
            e.HasKey(x => new { x.UserId, x.Category });
        });
        b.Entity<DeviceToken>(e =>
        {
            e.ToTable("device_tokens");
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
        });
        b.Entity<Template>(e =>
        {
            e.ToTable("templates");
            e.HasKey(x => new { x.Key, x.Lang, x.Channel });
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        });

        // Transactional outbox/inbox (docs/adr/0020-outbox-inbox.md)
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}
