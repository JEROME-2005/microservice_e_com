using System.Text.Json;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using LankaMart.Notifications.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Delivery;

/// <summary>Claim → send → back off. Safe with several replicas: <c>FOR UPDATE SKIP LOCKED</c> lets each replica lock a DIFFERENT batch
/// instantly; the batch is marked <c>Sending</c> with a 2-minute LEASE and committed, and the slow network calls happen OUTSIDE the
/// transaction. If a worker dies mid-send its lease expires and another worker re-claims the rows. Competing consumers, no coordinator.
/// Failures back off 1m → 5m → 30m → 2h → 12h, then DeadLetter (visible to admins, can be requeued).</summary>
public sealed class DeliveryWorker(IServiceScopeFactory scopes, IConfiguration cfg, TimeProvider clock, ILogger<DeliveryWorker> log) : BackgroundService
{
    public static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(cfg.GetValue("Delivery:PollSeconds", 5)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                while (await RunBatchAsync(stoppingToken) > 0)
                {
                    // keep draining while there is work
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Delivery loop failed");                   // the loop must never die
            }
        }
    }

    /// <summary>Claims and delivers one batch; returns how many messages were claimed (public so tests can drive it with a fake clock).</summary>
    public async Task<int> RunBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<NotificationsDb>();
        var batch = await ClaimAsync(db, ct);
        var maxAttempts = cfg.GetValue("Delivery:MaxAttempts", 5);
        foreach (var m in batch)
        {
            var now = clock.GetUtcNow();
            try
            {
                var outcome = await SendOneAsync(sp, db, m, ct);
                await db.Messages.Where(x => x.Id == m.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, outcome)
                    .SetProperty(x => x.SentAt, now)
                    .SetProperty(x => x.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(x => x.LastError, (string?)null), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var attempts = m.Attempts + 1;
                var dead = attempts >= maxAttempts;
                var next = now + Backoff[Math.Min(attempts - 1, Backoff.Length - 1)];
                var err = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                log.LogWarning("Notification {Id} ({Channel}) attempt {Attempts} failed: {Error}", m.Id, m.Channel, attempts, err);
                await db.Messages.Where(x => x.Id == m.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Attempts, attempts)
                    .SetProperty(x => x.LastError, err)
                    .SetProperty(x => x.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(x => x.Status, dead ? DeliveryStatus.DeadLetter : DeliveryStatus.Pending)
                    .SetProperty(x => x.NextAttemptAt, next), ct);
            }
        }
        return batch.Count;
    }

    private async Task<List<NotificationMessage>> ClaimAsync(NotificationsDb db, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var size = cfg.GetValue("Delivery:BatchSize", 20);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Schema-qualified on purpose (the service login has no search_path into "notifications" outside psql). FromSql parameterises
        // {now} and {size}; rows locked by another replica are SKIPPED, not waited for.
        var ids = await db.Messages.FromSql($"""
                SELECT * FROM notifications.notification_messages
                WHERE (status = 'Pending' AND next_attempt_at <= {now}) OR (status = 'Sending' AND locked_until < {now})
                ORDER BY next_attempt_at
                LIMIT {size}
                FOR UPDATE SKIP LOCKED
                """).Select(m => m.Id).ToListAsync(ct);
        if (ids.Count == 0)
        {
            await tx.CommitAsync(ct);
            return [];
        }
        var lease = now + Lease;
        await db.Messages.Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, DeliveryStatus.Sending).SetProperty(m => m.LockedUntil, lease), ct);
        await tx.CommitAsync(ct);
        return await db.Messages.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(ct);
    }

    private static async Task<DeliveryStatus> SendOneAsync(IServiceProvider sp, NotificationsDb db, NotificationMessage m, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<NotificationService>();
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == m.UserId, ct)
                      ?? throw new InvalidOperationException("Contact missing");
        var tpl = await svc.FindTemplateAsync(m.TemplateKey, contact.Language, m.Channel, ct)
                  ?? throw new InvalidOperationException($"Template {m.TemplateKey}/{m.Channel} missing");
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(m.DataJson) ?? [];
        switch (m.Channel)
        {
            case Channel.Email:
                await sp.GetRequiredService<IEmailSender>().SendAsync(contact.Email,
                    TemplateRenderer.Render(tpl.Subject ?? "", data, html: false), TemplateRenderer.Render(tpl.Body, data, html: true), ct);
                break;
            case Channel.Sms:
                await sp.GetRequiredService<ISmsSender>().SendAsync(contact.Phone, TemplateRenderer.Render(tpl.Body, data, html: false), ct);
                break;
            case Channel.Push:
                var tokens = await db.Devices.AsNoTracking().Where(d => d.UserId == m.UserId).Select(d => d.Token).ToListAsync(ct);
                if (tokens.Count == 0)
                {
                    return DeliveryStatus.Skipped;                       // no registered device: not an error
                }
                foreach (var t in tokens)
                {
                    await sp.GetRequiredService<IPushSender>().SendAsync(t,
                        TemplateRenderer.Render(tpl.Subject ?? "", data, html: false), TemplateRenderer.Render(tpl.Body, data, html: false), ct);
                }
                break;
            case Channel.InApp:
                break;                                                   // created at enqueue time; never queued
        }
        return DeliveryStatus.Sent;
    }
}
