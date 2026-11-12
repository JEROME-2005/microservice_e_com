namespace LankaMart.Notifications.Api.Domain;

public enum Channel { Email, Sms, Push, InApp }

public enum DeliveryStatus { Pending, Sending, Sent, Failed, DeadLetter, Skipped }

/// <summary>Local read-model: who to contact and in which language. Built from <c>UserRegistered</c> — Notifications never
/// calls Users/Identity to look people up (same pattern as Catalog's <c>known_sellers</c>).</summary>
public class Contact
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Language { get; set; } = "en";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One outbound message on one channel. The text is rendered at SEND time from <see cref="DataJson"/>, so a retry uses the template as fixed.</summary>
public class NotificationMessage
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Channel Channel { get; set; }
    public string TemplateKey { get; set; } = "";
    public string Category { get; set; } = "system";
    public string DataJson { get; set; } = "{}";                 // placeholder values
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }             // lease: a crashed worker's claim expires and another worker takes over
    public string? LastError { get; set; }
    public string? DedupeKey { get; set; }                       // unique → the same event can't notify twice
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>In-app inbox (the bell). Permanent record of everything we told the user.</summary>
public class InboxItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? DeepLink { get; set; }
    public string Category { get; set; } = "system";
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Preference
{
    public Guid UserId { get; set; }
    public string Category { get; set; } = "";
    public bool Email { get; set; }
    public bool Sms { get; set; }
    public bool Push { get; set; }
    public bool InApp { get; set; }

    public bool Allows(Channel c) => c switch
    {
        Channel.Email => Email,
        Channel.Sms => Sms,
        Channel.Push => Push,
        _ => InApp
    };
}

/// <summary>Channel policy (System Scenarios §7): email = transactional, SMS = OTP/security only (it costs money),
/// push = order status/promos, in-app = everything.</summary>
public static class PreferenceDefaults
{
    public static readonly string[] UserEditable = ["orders", "promos", "system"];     // "security" (OTP, password changes) can NEVER be switched off

    public static Preference For(Guid userId, string category) => category switch
    {
        "security" => new() { UserId = userId, Category = category, Email = true, Sms = true, Push = true, InApp = true },
        "promos" => new() { UserId = userId, Category = category, Email = false, Sms = false, Push = false, InApp = true },   // marketing is opt-IN
        _ => new() { UserId = userId, Category = category, Email = true, Sms = false, Push = true, InApp = true }              // SMS stays off: it costs money
    };
}

public class DeviceToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = "";
    public string Platform { get; set; } = "android";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Templates are DATA, not code (Phase 3 moves them into the Localization service).</summary>
public class Template
{
    public string Key { get; set; } = "";
    public string Lang { get; set; } = "en";
    public Channel Channel { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
}
