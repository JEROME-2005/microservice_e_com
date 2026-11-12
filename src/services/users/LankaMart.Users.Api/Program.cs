using FluentValidation;
using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("users");

builder.Services.AddDbContext<UsersDb>(o => o
    .UseNpgsql(cfg.GetConnectionString("Default"), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "users"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "users", x => x.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();
if (await app.RunMigrationsIfRequestedAsync<UsersDb>(args))
{
    return;   // `dotnet X.dll --migrate-only` = run migrations, then exit with code 0 (Kubernetes migration Job)
}
app.UsePlatformDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapCustomers();
app.MapSellers();
app.Run();

public partial class Program;
