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
builder.Services.AddSingleton<IOtpSender, LogOtpSender>();
builder.Services.AddSingleton<OtpService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddPlatformPostgresHealth(cfg);
builder.Services.AddPlatformRedis(cfg);
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformMessaging(cfg, "identity");          // Identity only publishes; no consumers

var app = builder.Build();
await app.MigrateDatabaseAsync<IdentityDb>();
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
