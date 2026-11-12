using FluentValidation;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Delivery;
using LankaMart.Notifications.Api.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("notifications");

builder.Services.AddDbContext<NotificationsDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"))
    .UseSnakeCaseNamingConvention());
builder.Services.Configure<EmailOptions>(cfg.GetSection("Email"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();
builder.Services.AddScoped<NotificationService>();

// Channel adapters, selected by configuration (no vendor SDK lock-in — docs/adr/0021-notification-service.md)
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
if (cfg["Sms:Provider"] == "http")
{
    builder.Services.AddScoped<ISmsSender, HttpSmsSender>();
}
else
{
    builder.Services.AddScoped<ISmsSender, LogSmsSender>();
}
builder.Services.AddScoped<IPushSender, LogPushSender>();

if (cfg.GetValue("Workers:Enabled", true))           // Kubernetes runs the loop in a separate "worker" Deployment (Sprint 4/5)
{
    builder.Services.AddHostedService<DeliveryWorker>();
}
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging<NotificationsDb>(cfg, "notifications", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
if (await app.RunMigrationsIfRequestedAsync<NotificationsDb>(args))
{
    return;   // `dotnet X.dll --migrate-only` = run migrations, then exit with code 0 (Kubernetes migration Job)
}
// Only when really serving (job mode returned above). In a fresh cluster the first normal start runs after the migration Job.
using (var scope = app.Services.CreateScope())
{
    await TemplateSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<NotificationsDb>());
}

app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapNotifications();
app.Run();

public partial class Program;
