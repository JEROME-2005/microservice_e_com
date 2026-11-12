using System.Threading.Channels;
using FluentValidation;
using LankaMart.Payments.Api.Data;
using LankaMart.Payments.Api.Features;
using LankaMart.Payments.Api.Gateway;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("payments");

builder.Services.AddDbContext<PaymentsDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "payments"))
    .UseSnakeCaseNamingConvention());
builder.Services.Configure<MockOptions>(cfg.GetSection("Mock"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Channel.CreateUnbounded<MockJob>());
builder.Services.AddSingleton<MockGateway>();
builder.Services.AddSingleton<MockWebhookSender>();
builder.Services.AddHostedService<MockWebhookDispatcher>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<WebhookProcessor>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "payments", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
if (await app.RunMigrationsIfRequestedAsync<PaymentsDb>(args))
{
    return;   // `dotnet X.dll --migrate-only` = run migrations, then exit with code 0 (Kubernetes migration Job)
}
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapPayments();
app.Run();

public partial class Program;
