using System.Text.Json;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Features;

public sealed class NotificationService(NotificationsDb db, TimeProvider clock, ILogger<NotificationService> log)
{
    /// <summary>Fallback chain: the user's language → English.</summary>
    public async Task<Template?> FindTemplateAsync(string key, string lang, Channel ch, CancellationToken ct) =>
        await db.Templates.AsNoTracking()
            .Where(t => t.Key == key && t.Channel == ch && (t.Lang == lang || t.Lang == "en"))
            .OrderBy(t => t.Lang == lang ? 0 : 1)
            .FirstOrDefaultAsync(ct);

    /// <summary>Decide WHICH channels this user wants for this category, create the in-app item immediately and queue the rest for the
    /// delivery worker (never send inline from a consumer: slow SMTP would block the queue and a retry would re-send).
    /// Idempotent per <paramref name="dedupe"/> (typically the event's message id). Inside a consumer the SaveChanges joins the
    /// consumer-outbox transaction, so the inbox record and these rows commit together.</summary>
    public async Task EnqueueAsync(Guid userId, string category, string templateKey, Dictionary<string, string> data, IEnumerable<Channel> channels,
                                   string? dedupe, string? deepLink = null, CancellationToken ct = default)
    {
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (contact is null)
        {
            log.LogWarning("No contact for {UserId}; skipping {Template}", userId, templateKey);   // UserRegistered not processed yet / staff account
            return;
        }
        var pref = await db.Preferences.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId && p.Category == category, ct)
                   ?? PreferenceDefaults.For(userId, category);
        var now = clock.GetUtcNow();

        foreach (var ch in channels.Distinct())
        {
            if (!pref.Allows(ch))
            {
                continue;                                                     // respect the user's choice (System Scenarios §7)
            }
            var tpl = await FindTemplateAsync(templateKey, contact.Language, ch, ct);
            if (tpl is null)
            {
                continue;
            }
            var key = dedupe is null ? null : $"{dedupe}:{ch}";
            if (key is not null && await db.Messages.AnyAsync(m => m.DedupeKey == key, ct))
            {
                continue;
            }

            var msg = new NotificationMessage
            {
                Id = Guid.CreateVersion7(), UserId = userId, Channel = ch, TemplateKey = templateKey, Category = category,
                DataJson = JsonSerializer.Serialize(data), CreatedAt = now, NextAttemptAt = now, DedupeKey = key
            };
            if (ch == Channel.InApp)
            {
                db.Inbox.Add(new InboxItem
                {
                    Id = Guid.CreateVersion7(), UserId = userId, Category = category, DeepLink = deepLink, CreatedAt = now,
                    Title = TemplateRenderer.Render(tpl.Subject ?? "", data, html: false),
                    Body = TemplateRenderer.Render(tpl.Body, data, html: false)
                });
                msg.Status = DeliveryStatus.Sent;                             // in-app needs no delivery step; the row stays for audit/dedupe
                msg.SentAt = now;
            }
            db.Messages.Add(msg);
        }
        await db.SaveChangesAsync(ct);
    }
}
