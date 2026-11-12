using System.Text.RegularExpressions;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LankaMart.Notifications.Api.Features;

/// <summary>Plain <c>{placeholder}</c> replacement — no scripting language, so no template-injection risk. Values are HTML-encoded for
/// email bodies (a customer named <c>&lt;script&gt;</c> can't inject markup); unknown placeholders render as empty.</summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    public static string Render(string template, IReadOnlyDictionary<string, string> data, bool html) =>
        Placeholder().Replace(template, m =>
        {
            var v = data.GetValueOrDefault(m.Groups[1].Value, "");
            return html ? System.Net.WebUtility.HtmlEncode(v) : v;
        });
}

public static class TemplateSeeder
{
    // (key, channel, subject, body) — English now; si/ta rows arrive with the Localization service in Phase 3
    private static readonly (string Key, Channel Channel, string? Subject, string Body)[] Seed =
    [
        ("auth.otp", Channel.Sms, null, "LankaMart: your verification code is {code}. It expires in 5 minutes. Never share it."),
        ("auth.otp", Channel.Email, "Your LankaMart verification code", "<p>Your verification code is <b>{code}</b>.</p><p>It expires in 5 minutes. If you didn't request it, ignore this email.</p>"),
        ("user.welcome", Channel.Email, "Welcome to LankaMart, {name}", "<p>Hi {name}, welcome to LankaMart! Start browsing anytime.</p>"),
        ("user.welcome", Channel.InApp, "Welcome, {name}!", "Your account is ready. Happy shopping."),
        ("order.paid", Channel.Email, "Order {orderNumber} confirmed", "<p>Thanks for your order <b>{orderNumber}</b>. We received your payment of <b>{total}</b>.</p>"),
        ("order.paid", Channel.InApp, "Order {orderNumber} confirmed", "Payment of {total} received. We'll keep you posted."),
        ("order.paid", Channel.Push, "Order confirmed", "Order {orderNumber} is confirmed."),
        ("order.status", Channel.InApp, "Order update", "{text}"),
        ("order.status", Channel.Push, "Order update", "{text}"),
        ("order.cancelled", Channel.InApp, "Order cancelled", "Your order was cancelled ({reason})."),
        ("payment.failed", Channel.InApp, "Payment failed", "Your payment didn't go through ({reason}). Your cart was restored — you can try again."),
        ("seller.approved", Channel.Email, "Your seller account is approved", "<p>Congratulations {name}! You can now list products on LankaMart.</p>"),
        ("seller.approved", Channel.InApp, "Seller account approved", "You can now list products."),
        ("seller.rejected", Channel.Email, "Your seller application needs changes", "<p>Hi {name}, your application was not approved (reason: {reason}). You can fix the details and resubmit.</p>"),
        ("seller.rejected", Channel.InApp, "Seller application needs changes", "Reason: {reason}. You can resubmit."),
        ("stock.low", Channel.InApp, "Low stock", "SKU {sku} is running low ({available} left)."),
    ];

    /// <summary>Idempotent: adds only missing rows, never overwrites wording an admin changed.</summary>
    public static async Task SeedAsync(NotificationsDb db, CancellationToken ct = default)
    {
        foreach (var (key, ch, subject, body) in Seed)
        {
            if (!await db.Templates.AnyAsync(t => t.Key == key && t.Lang == "en" && t.Channel == ch, ct))
            {
                db.Templates.Add(new Template { Key = key, Lang = "en", Channel = ch, Subject = subject, Body = body });
            }
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();         // two replicas started at the same moment; the other one seeded — nothing left to do
        }
    }
}
