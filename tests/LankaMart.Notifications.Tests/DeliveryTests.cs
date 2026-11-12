using System.Text.Json;
using LankaMart.Notifications.Api.Delivery;
using LankaMart.Notifications.Api.Domain;
using LankaMart.Notifications.Api.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.Notifications.Tests;

public sealed class EnqueueTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private async Task EnqueueAsync(IServiceProvider sp, Guid userId, string category, string template, Channel[] channels, string? dedupe)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationService>()
            .EnqueueAsync(userId, category, template, new() { ["orderNumber"] = "LM-1", ["total"] = "LKR 10.00" }, channels, dedupe);
    }

    [Fact]
    public async Task Same_dedupe_key_twice_creates_one_row_per_channel()
    {
        await using var sp = pg.BuildServices(TimeProvider.System, new FakeEmailSender());
        var user = await pg.AddContactAsync();
        await EnqueueAsync(sp, user, "orders", "order.paid", [Channel.Email, Channel.InApp], "msg-1");
        await EnqueueAsync(sp, user, "orders", "order.paid", [Channel.Email, Channel.InApp], "msg-1");

        await using var db = pg.NewDb();
        Assert.Equal(2, await db.Messages.CountAsync(m => m.UserId == user));
        Assert.Equal(1, await db.Inbox.CountAsync(i => i.UserId == user));
    }

    [Fact]
    public async Task User_without_a_contact_is_skipped()
    {
        await using var sp = pg.BuildServices(TimeProvider.System, new FakeEmailSender());
        var stranger = Guid.NewGuid();
        await EnqueueAsync(sp, stranger, "orders", "order.paid", [Channel.Email, Channel.InApp], "msg-x");

        await using var db = pg.NewDb();
        Assert.Equal(0, await db.Messages.CountAsync(m => m.UserId == stranger));
    }

    [Fact]
    public async Task Preferences_are_respected_email_off_still_shows_in_app()
    {
        await using var sp = pg.BuildServices(TimeProvider.System, new FakeEmailSender());
        var user = await pg.AddContactAsync();
        await using (var db = pg.NewDb())
        {
            db.Preferences.Add(new Preference { UserId = user, Category = "orders", Email = false, Sms = false, Push = true, InApp = true });
            await db.SaveChangesAsync();
        }
        await EnqueueAsync(sp, user, "orders", "order.paid", [Channel.Email, Channel.InApp], "msg-2");

        await using var check = pg.NewDb();
        var channels = await check.Messages.Where(m => m.UserId == user).Select(m => m.Channel).ToListAsync();
        Assert.Equal([Channel.InApp], channels);
    }

    [Fact]
    public async Task In_app_item_is_created_immediately_and_unknown_language_falls_back_to_english()
    {
        await using var sp = pg.BuildServices(TimeProvider.System, new FakeEmailSender());
        var user = await pg.AddContactAsync(language: "si");                 // no Sinhala templates yet → English
        await EnqueueAsync(sp, user, "orders", "order.paid", [Channel.InApp], "msg-3");

        await using var db = pg.NewDb();
        var item = await db.Inbox.SingleAsync(i => i.UserId == user);
        Assert.Equal("Order LM-1 confirmed", item.Title);
        Assert.Equal(DeliveryStatus.Sent, await db.Messages.Where(m => m.UserId == user).Select(m => m.Status).SingleAsync());
    }
}

public sealed class DeliveryWorkerTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private async Task<Guid> QueueEmailAsync(Guid userId, DateTimeOffset now, string name = "Nimal")
    {
        await using var db = pg.NewDb();
        var id = Guid.CreateVersion7();
        db.Messages.Add(new NotificationMessage
        {
            Id = id, UserId = userId, Channel = Channel.Email, TemplateKey = "user.welcome", Category = "system",
            DataJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = name }), CreatedAt = now, NextAttemptAt = now
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Successful_send_marks_the_message_sent_and_renders_html_safely()
    {
        var clock = Clock.StartingNow();
        var email = new FakeEmailSender();
        await using var sp = pg.BuildServices(clock, email);
        var user = await pg.AddContactAsync();
        var id = await QueueEmailAsync(user, clock.GetUtcNow(), name: "<b>Kamal</b>");

        await DrainAsync(sp.GetRequiredService<DeliveryWorker>());

        await using var db = pg.NewDb();
        var m = await db.Messages.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(DeliveryStatus.Sent, m.Status);
        var sent = Assert.Single(email.Sent, s => s.To == $"{user:N}@test.local");
        Assert.Contains("&lt;b&gt;Kamal&lt;/b&gt;", sent.Body);
    }

    [Fact]
    public async Task Failures_back_off_then_dead_letter_after_max_attempts()
    {
        var clock = Clock.StartingNow();
        await using var sp = pg.BuildServices(clock, new FakeEmailSender(fail: true), maxAttempts: 5);
        var worker = sp.GetRequiredService<DeliveryWorker>();
        var user = await pg.AddContactAsync();
        var id = await QueueEmailAsync(user, clock.GetUtcNow());

        var previousNext = DateTimeOffset.MinValue;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(1, await worker.RunBatchAsync(CancellationToken.None));
            await using var db = pg.NewDb();
            var m = await db.Messages.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(attempt, m.Attempts);
            Assert.Equal(attempt < 5 ? DeliveryStatus.Pending : DeliveryStatus.DeadLetter, m.Status);
            Assert.NotNull(m.LastError);
            Assert.Null(m.LockedUntil);
            var expectedDelay = DeliveryWorker.Backoff[attempt - 1];
            Assert.InRange((m.NextAttemptAt - clock.GetUtcNow()).TotalSeconds, expectedDelay.TotalSeconds - 1, expectedDelay.TotalSeconds + 1);
            Assert.True(m.NextAttemptAt > previousNext);                     // the wait grows: 1m → 5m → 30m → 2h → 12h
            previousNext = m.NextAttemptAt;

            Assert.Equal(0, await worker.RunBatchAsync(CancellationToken.None));   // not due yet → nothing claimed
            clock.SetUtcNow(m.NextAttemptAt.AddSeconds(1));                   // time passes until the next attempt is due
        }

        Assert.Equal(0, await worker.RunBatchAsync(CancellationToken.None));  // dead-lettered messages are never claimed again
    }

    [Fact]
    public async Task Expired_lease_of_a_crashed_worker_is_reclaimed()
    {
        var clock = Clock.StartingNow();
        var email = new FakeEmailSender();
        await using var sp = pg.BuildServices(clock, email);
        var user = await pg.AddContactAsync();
        var id = await QueueEmailAsync(user, clock.GetUtcNow());
        await using (var db = pg.NewDb())                                        // a worker claimed it, then died before sending
        {
            await db.Messages.Where(m => m.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, DeliveryStatus.Sending).SetProperty(m => m.LockedUntil, clock.GetUtcNow() + DeliveryWorker.Lease));
        }
        var worker = sp.GetRequiredService<DeliveryWorker>();

        Assert.Equal(0, await worker.RunBatchAsync(CancellationToken.None));     // lease still valid → hands off
        clock.Advance(DeliveryWorker.Lease + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await worker.RunBatchAsync(CancellationToken.None));     // lease expired → re-claimed and sent

        await using var check = pg.NewDb();
        Assert.Equal(DeliveryStatus.Sent, await check.Messages.Where(m => m.Id == id).Select(m => m.Status).SingleAsync());
    }

    [Fact]
    public async Task Two_concurrent_workers_send_each_of_100_messages_exactly_once()
    {
        var clock = Clock.StartingNow();
        var email = new FakeEmailSender(delayMs: 5);                             // slow-ish SMTP so the two workers really overlap
        await using var sp = pg.BuildServices(clock, email, batchSize: 7);
        var user = await pg.AddContactAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 100; i++)
        {
            await QueueEmailAsync(user, clock.GetUtcNow(), name: $"{tag}-{i}");
        }

        // two workers = two replicas (separate scopes, separate DB connections, same table)
        var w1 = new DeliveryWorker(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeliveryWorker>.Instance);
        var w2 = new DeliveryWorker(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeliveryWorker>.Instance);
        await Task.WhenAll(DrainAsync(w1), DrainAsync(w2));

        var mine = email.Sent.Where(s => s.Subject.Contains(tag, StringComparison.Ordinal)).Select(s => s.Subject).ToList();
        Assert.Equal(100, mine.Count);                                           // nothing lost …
        Assert.Equal(100, mine.Distinct().Count());                              // … and nothing sent twice
        await using var db = pg.NewDb();
        Assert.Equal(100, await db.Messages.CountAsync(m => m.UserId == user && m.Status == DeliveryStatus.Sent));
    }

    private static async Task DrainAsync(DeliveryWorker worker)
    {
        while (await worker.RunBatchAsync(CancellationToken.None) > 0)
        {
        }
    }
}
