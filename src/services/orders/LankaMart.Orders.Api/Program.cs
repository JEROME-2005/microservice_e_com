using FluentValidation;
using LankaMart.Orders.Api.Clients;
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Features;
using LankaMart.Orders.Api.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("orders");

builder.Services.AddDbContext<OrdersDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "orders"))
    .UseSnakeCaseNamingConvention());
builder.Services.Configure<OrdersOptions>(cfg.GetSection("Orders"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInternalClient<ICatalogClient, CatalogClient>(cfg, "Catalog");
builder.Services.AddInternalClient<IInventoryClient, InventoryClient>(cfg, "Inventory");
builder.Services.AddInternalClient<IPaymentsClient, PaymentsClient>(cfg, "Payments");
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddHostedService<ExpireStaleOrdersWorker>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "orders", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<OrdersDb>();
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapOrders();
app.Run();

public partial class Program;
