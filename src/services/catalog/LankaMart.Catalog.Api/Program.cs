using FluentValidation;
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("catalog");

builder.Services.AddDbContext<CatalogDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "catalog", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
await app.MigrateDatabaseAsync<CatalogDb>();
using (var scope = app.Services.CreateScope())
{
    await CatalogSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<CatalogDb>());
}

app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapPublic();
app.MapSeller();
app.MapInternal();
app.Run();

public partial class Program;
