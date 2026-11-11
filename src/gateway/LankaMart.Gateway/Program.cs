using System.Diagnostics;
using System.Threading.RateLimiting;
using LankaMart.BuildingBlocks;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
builder.AddPlatformDefaults("gateway");
builder.Services.AddPlatformAuth(cfg);
builder.Services.AddPlatformRedis(cfg);           // used for the logout blacklist
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_000_000);   // 1 MB: JSON APIs never need more (uploads bypass the gateway in Phase 3)

// ---- CORS: which browser origins may call us. (In dev the Angular proxy makes this moot, but other origins need it.) ----
var origins = cfg.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("X-Request-Id").SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

// ---- Rate limiting: a global safety net + a strict policy for login/OTP ----
static string Client(HttpContext c) => c.Request.Headers["CF-Connecting-IP"].FirstOrDefault()   // behind Cloudflare (Phase 6)
                                       ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
var authPerMinute = cfg.GetValue("RateLimits:AuthPerMinute", 10);
var globalPerMinute = cfg.GetValue("RateLimits:GlobalPerMinute", 300);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Client(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth", c =>
        RateLimitPartition.GetFixedWindowLimiter(Client(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddReverseProxy().LoadFromConfig(cfg.GetSection("ReverseProxy"));

var app = builder.Build();
app.UsePlatformDefaults();

app.Use(async (ctx, next) =>       // security headers + a request id the client can quote when reporting problems
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Request-Id"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    await next();
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();

// Logout blacklist: a logged-out access token is dead immediately (not in up-to-15-minutes).
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindFirst("jti")?.Value is { } jti)
    {
        var redis = ctx.RequestServices.GetRequiredService<IConnectionMultiplexer>();
        if (await redis.GetDatabase().KeyExistsAsync($"bl:{jti}"))
        {
            ctx.Response.StatusCode = 401;
            return;
        }
    }
    await next();
});

app.UseAuthorization();
app.MapReverseProxy();

// Anything no route matches (including /api/v1/internal/**) is a plain 404 — even without a token.
// Without this, the fail-closed fallback policy would answer 401 for unmatched paths when anonymous.
app.MapFallback("{**path}", () => Results.Problem(statusCode: 404, title: "Not Found",
    extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" })).AllowAnonymous();
app.Run();

public partial class Program;
