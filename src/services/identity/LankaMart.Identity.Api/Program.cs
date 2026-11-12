using FluentValidation;
using LankaMart.Identity.Api.Data;
using LankaMart.Identity.Api.Domain;
using LankaMart.Identity.Api.Features;
using LankaMart.Identity.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("identity");

builder.Services.AddDbContext<IdentityDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 10;
    o.Password.RequireDigit = true;
    o.Password.RequireLowercase = true;
    o.Password.RequireUppercase = true;
    o.Password.RequireNonAlphanumeric = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<IdentityDb>();

builder.Services.Configure<JwtIssuingOptions>(cfg.GetSection("Jwt"));
builder.Services.Configure<AuthOptions>(cfg.GetSection("Auth"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RsaKeyProvider>();
if (cfg["Otp:Sender"] == "notification")
{
    // Real delivery through the Notification service. Short resilience budget: the user is waiting, and a failure is non-fatal
    // (OtpService logs it and lets the user resend) — the default ~30 s retry budget would make registration hang.
    builder.Services.AddInternalClient<INotificationsClient, NotificationsClient>(cfg, "Notifications", r =>
    {
        r.Retry.MaxRetryAttempts = 1;
        r.Retry.Delay = TimeSpan.FromMilliseconds(300);
        r.AttemptTimeout.Timeout = TimeSpan.FromSeconds(4);
        r.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        r.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(9);
    });
    builder.Services.AddScoped<IOtpSender, NotificationOtpSender>();
}
else
{
    builder.Services.AddScoped<IOtpSender, LogOtpSender>();
}
builder.Services.AddScoped<OtpService>();            // was Singleton — it now depends on a scoped sender
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging<IdentityDb>(cfg, "identity");          // Identity only publishes; no consumers

var app = builder.Build();
if (await app.RunMigrationsIfRequestedAsync<IdentityDb>(args))
{
    return;   // `dotnet X.dll --migrate-only` = run migrations, then exit with code 0 (Kubernetes migration Job)
}
await IdentitySeeder.SeedAsync(app.Services, app.Environment, cfg);

app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapWellKnown();
app.MapAuth();
if (app.Environment.IsDevelopment())
{
    app.MapDevEndpoints();
}
app.Run();

public partial class Program;     // lets integration tests (WebApplicationFactory<Program>) see this class
