using System.Net.Http.Json;

namespace LankaMart.Identity.Api.Security;

/// <summary>Internal REST client for the Notification service (never routed by the gateway; internal key added by the handler).</summary>
public interface INotificationsClient
{
    Task SendOtpAsync(string channel, string destination, string code, CancellationToken ct);
}

public sealed class NotificationsClient(HttpClient http) : INotificationsClient
{
    public async Task SendOtpAsync(string channel, string destination, string code, CancellationToken ct) =>
        // Language "en" until the Localization service (Phase 3) brings si/ta templates.
        (await http.PostAsJsonAsync("/api/v1/internal/notifications/otp", new SendOtpRequest(channel, destination, code, "en"), ct)).EnsureSuccessStatusCode();
}

/// <summary><c>Otp:Sender=notification</c>: OTPs are really SENT (email via SMTP/Mailpit, SMS via the SMS adapter) by the Notification service.
/// A synchronous call on purpose — the code must never travel over the event bus (docs/adr/0021-notification-service.md).</summary>
public sealed class NotificationOtpSender(INotificationsClient client) : IOtpSender
{
    public Task SendAsync(string channel, string destination, string code) => client.SendOtpAsync(channel, destination, code, CancellationToken.None);
}
