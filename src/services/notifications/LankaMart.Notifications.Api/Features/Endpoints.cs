using FluentValidation;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Delivery;
using LankaMart.Notifications.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Features;

public sealed record PreferenceDto(string Category, bool Email, bool Sms, bool Push, bool InApp);
public sealed record DeviceRequest(string Token, string Platform);

public sealed class DeviceValidator : AbstractValidator<DeviceRequest>
{
    public DeviceValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Platform).Must(p => p is "android" or "ios" or "web");
    }
}

public sealed class PreferenceValidator : AbstractValidator<PreferenceDto>
{
    public PreferenceValidator() =>
        RuleFor(x => x.Category).Must(c => PreferenceDefaults.UserEditable.Contains(c))
            .WithMessage("Category must be one of: orders, promos, system. Security notifications can't be switched off.");
}

public sealed class SendOtpValidator : AbstractValidator<SendOtpRequest>
{
    public SendOtpValidator()
    {
        RuleFor(x => x.Channel).Must(c => c is "email" or "phone");
        RuleFor(x => x.Destination).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).Matches(@"^\d{6}$");
    }
}

public static class Endpoints
{
    public static void MapNotifications(this WebApplication app)
    {
        // ───── ME: always the caller's own data (user id from the token, never from the URL → IDOR-proof) ─────
        var me = app.MapGroup("/api/v1/me").WithTags("Notifications").RequireAuthorization();

        me.MapGet("/notifications", async (HttpContext c, NotificationsDb db, bool? unread, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            var uid = c.User.UserId();
            var q = db.Inbox.AsNoTracking().Where(i => i.UserId == uid);
            if (unread == true)
            {
                q = q.Where(i => i.ReadAt == null);
            }
            var total = await q.LongCountAsync(ct);
            var items = await q.OrderByDescending(i => i.CreatedAt).Skip((p - 1) * size).Take(size)
                .Select(i => new { i.Id, i.Title, i.Body, i.DeepLink, i.Category, read = i.ReadAt != null, i.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), p, size, total));
        });

        me.MapGet("/notifications/unread-count", async (HttpContext c, NotificationsDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            return Results.Ok(new { count = await db.Inbox.CountAsync(i => i.UserId == uid && i.ReadAt == null, ct) });
        });

        me.MapPost("/notifications/{id:guid}/read", async (Guid id, HttpContext c, NotificationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            await db.Inbox.Where(i => i.Id == id && i.UserId == uid && i.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReadAt, clock.GetUtcNow()), ct);
            return Results.NoContent();                                    // idempotent; someone else's id is simply a no-op
        });

        me.MapPost("/notifications/read-all", async (HttpContext c, NotificationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            await db.Inbox.Where(i => i.UserId == uid && i.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(i => i.ReadAt, clock.GetUtcNow()), ct);
            return Results.NoContent();
        });

        me.MapGet("/notification-preferences", async (HttpContext c, NotificationsDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var saved = await db.Preferences.AsNoTracking().Where(p => p.UserId == uid).ToListAsync(ct);
            return Results.Ok(PreferenceDefaults.UserEditable
                .Select(cat => saved.FirstOrDefault(p => p.Category == cat) ?? PreferenceDefaults.For(uid, cat))
                .Select(p => new PreferenceDto(p.Category, p.Email, p.Sms, p.Push, p.InApp)));
        });

        me.MapPut("/notification-preferences", async (PreferenceDto r, HttpContext c, NotificationsDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var p = await db.Preferences.SingleOrDefaultAsync(x => x.UserId == uid && x.Category == r.Category, ct);
            if (p is null)
            {
                p = new Preference { UserId = uid, Category = r.Category };
                db.Preferences.Add(p);
            }
            p.Email = r.Email;
            p.Sms = r.Sms;
            p.Push = r.Push;
            p.InApp = r.InApp;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithValidation<PreferenceDto>();

        me.MapPost("/devices", async (DeviceRequest r, HttpContext c, NotificationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var d = await db.Devices.SingleOrDefaultAsync(x => x.Token == r.Token, ct);
            if (d is null)
            {
                db.Devices.Add(new DeviceToken { Id = Guid.CreateVersion7(), UserId = uid, Token = r.Token, Platform = r.Platform, CreatedAt = clock.GetUtcNow() });
            }
            else
            {
                d.UserId = uid;                                            // device changed hands (logout, then login as someone else)
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithValidation<DeviceRequest>();

        // ───── ADMIN: dead-letter queue (messages that failed MaxAttempts times) ─────
        var admin = app.MapGroup("/api/v1/admin/notifications").WithTags("Admin: Notifications")
            .RequireAuthorization(p => p.RequireRole(Roles.CustomerManager, Roles.SuperAdmin));

        admin.MapGet("/dead-letter", async (NotificationsDb db, CancellationToken ct) =>
            Results.Ok(await db.Messages.AsNoTracking().Where(m => m.Status == DeliveryStatus.DeadLetter).OrderByDescending(m => m.CreatedAt).Take(100)
                .Select(m => new { m.Id, m.UserId, channel = m.Channel.ToString(), m.TemplateKey, m.Attempts, m.LastError, m.CreatedAt }).ToListAsync(ct)));

        admin.MapPost("/{id:guid}/requeue", async (Guid id, NotificationsDb db, TimeProvider clock, ILogger<Program> log, HttpContext c, CancellationToken ct) =>
        {
            var n = await db.Messages.Where(m => m.Id == id && m.Status == DeliveryStatus.DeadLetter)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, DeliveryStatus.Pending)
                                          .SetProperty(m => m.Attempts, 0)
                                          .SetProperty(m => m.NextAttemptAt, clock.GetUtcNow()), ct);
            if (n == 0)
            {
                throw new NotFoundException();
            }
            log.LogInformation("AUDIT notification.requeue admin={Admin} message={Message}", c.User.UserId(), id);
            return Results.NoContent();
        });

        // ───── INTERNAL: OTP. Synchronous, immediate, and the code is NEVER written to the database. ─────
        app.MapInternalGroup("/api/v1/internal/notifications").MapPost("/otp", async (SendOtpRequest r, NotificationService svc, IServiceProvider sp, CancellationToken ct) =>
        {
            var ch = r.Channel == "email" ? Channel.Email : Channel.Sms;
            var tpl = await svc.FindTemplateAsync("auth.otp", Lang.Normalize(r.Language), ch, ct) ?? throw new NotFoundException("Template missing");
            var data = new Dictionary<string, string> { ["code"] = r.Code };
            if (ch == Channel.Email)
            {
                await sp.GetRequiredService<IEmailSender>().SendAsync(r.Destination,
                    TemplateRenderer.Render(tpl.Subject ?? "", data, html: false), TemplateRenderer.Render(tpl.Body, data, html: true), ct);
            }
            else
            {
                await sp.GetRequiredService<ISmsSender>().SendAsync(r.Destination, TemplateRenderer.Render(tpl.Body, data, html: false), ct);
            }
            return Results.Accepted();                                     // failure → exception → 5xx → Identity lets the user resend
        }).WithValidation<SendOtpRequest>();
    }
}
