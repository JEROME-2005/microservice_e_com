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
builder.Services.AddHostedService<ReservationSweeper>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "inventory", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<InventoryDb>();
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapInventory();
app.Run();

public partial class Program;
