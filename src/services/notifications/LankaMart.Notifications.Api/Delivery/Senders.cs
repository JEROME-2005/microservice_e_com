using System.Net.Http.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LankaMart.Notifications.Api.Delivery;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct);
}

public interface ISmsSender
{
    Task SendAsync(string toE164, string text, CancellationToken ct);
}

public interface IPushSender
{
    Task SendAsync(string deviceToken, string title, string body, CancellationToken ct);
}

public sealed class EmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseTls { get; set; }
    public string User { get; set; } = "";
    public string Password { get; set; } = "";     // secret: environment / Kubernetes Secret, never appsettings.json
    public string From { get; set; } = "LankaMart <no-reply@lankamart.local>";
}

/// <summary>Plain SMTP. Mailpit locally; in production point it at SendGrid's or Amazon SES's SMTP relay — no vendor SDK, no code change.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> opt) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        var o = opt.Value;
        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(o.From));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.Host, o.Port, o.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(o.User))
        {
            await smtp.AuthenticateAsync(o.User, o.Password, ct);
        }
        await smtp.SendAsync(new MailKit.SendRequest(msg), ct);     // MailKit 5: messages are wrapped in a SendRequest
        await smtp.DisconnectAsync(true, ct);
    }
}

/// <summary>Dev/staging SMS: logs the message with the phone number masked. Never use it in production — the body may contain an OTP.</summary>
public sealed class LogSmsSender(ILogger<LogSmsSender> log) : ISmsSender
{
    public Task SendAsync(string toE164, string text, CancellationToken ct)
    {
        log.LogWarning("[DEV SMS] to {To}: {Text}", toE164[..Math.Min(6, toE164.Length)] + "****", text);
        return Task.CompletedTask;
    }
}

/// <summary>Generic REST SMS aggregator adapter (<c>Sms:Provider=http</c>). Adjust the JSON shape to your provider's API
/// (Dialog/Mobitel/others publish docs). A SOAP-only provider gets the same interface with a generated client.</summary>
public sealed class HttpSmsSender(IHttpClientFactory f, IConfiguration cfg) : ISmsSender
{
    public async Task SendAsync(string toE164, string text, CancellationToken ct)
    {
        var http = f.CreateClient("sms");
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg["Sms:Url"]) { Content = JsonContent.Create(new { to = toE164, message = text }) };
        req.Headers.Authorization = new("Bearer", cfg["Sms:ApiKey"]);
        (await http.SendAsync(req, ct)).EnsureSuccessStatusCode();
    }
}

/// <summary>Dev push: logs. Real Firebase Cloud Messaging (<c>FcmPushSender</c>, <c>Push:Provider=fcm</c>) arrives with the Flutter app (Phase 7).</summary>
public sealed class LogPushSender(ILogger<LogPushSender> log) : IPushSender
{
    public Task SendAsync(string deviceToken, string title, string body, CancellationToken ct)
    {
        log.LogInformation("[DEV PUSH] {Title}: {Body}", title, body);
        return Task.CompletedTask;
    }
}
