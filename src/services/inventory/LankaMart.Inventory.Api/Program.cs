using FluentValidation;
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Features;
using LankaMart.Inventory.Api.Stock;
using LankaMart.Inventory.Api.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("inventory");

builder.Services.AddDbContext<InventoryDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RedisStock>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<ReconcilerService>();
// Background loops can run in a separate Kubernetes "worker" Deployment (Workers__Enabled=false on the API pods). They are safe on
// every replica (atomic status flips, compare-and-set), so this is about efficiency and isolation, not correctness.
if (cfg.GetValue("Workers:Enabled", true))
{
    builder.Services.AddHostedService<ReservationSweeper>();
    builder.Services.AddHostedService<StockReconciler>();
}
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging<InventoryDb>(cfg, "inventory", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
if (await app.RunMigrationsIfRequestedAsync<InventoryDb>(args))
{
    return;   // `dotnet X.dll --migrate-only` = run migrations, then exit with code 0 (Kubernetes migration Job)
}
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapInventory();
app.Run();

public partial class Program;
