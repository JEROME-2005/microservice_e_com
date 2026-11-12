using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Delivery;
using LankaMart.Notifications.Api.Domain;
using LankaMart.Notifications.Api.Features;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace LankaMart.Notifications.Tests;

/// <summary>Throwaway Postgres 17 (needs Docker) with the real Notifications migrations and templates. Each test uses fresh user ids.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("notifications_db").Build();

    public string ConnectionString => _pg.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        await TemplateSeeder.SeedAsync(db);
    }

    public NotificationsDb NewDb() => new(new DbContextOptionsBuilder<NotificationsDb>()
        .UseNpgsql(ConnectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"))
        .UseSnakeCaseNamingConvention().Options);

    /// <summary>The services the delivery worker and NotificationService need, with fakes for the outside world.</summary>
    public ServiceProvider BuildServices(TimeProvider clock, IEmailSender email, int batchSize = 20, int maxAttempts = 5)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Delivery:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Delivery:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build());
        services.AddDbContext<NotificationsDb>(o => o
            .UseNpgsql(ConnectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<NotificationService>();
        services.AddSingleton(email);
        services.AddScoped<ISmsSender, LogSmsSender>();
        services.AddScoped<IPushSender, LogPushSender>();
        services.AddSingleton<DeliveryWorker>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    public async Task<Guid> AddContactAsync(string language = "en")
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.Contacts.Add(new Contact { UserId = id, Email = $"{id:N}@test.local", Phone = "+94771234567", Language = language, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();
}

public static class Clock
{
    /// <summary>A controllable clock that starts at "now" (rows it stamps are realistic) — tests move it with Advance/SetUtcNow.</summary>
    public static FakeTimeProvider StartingNow() => new(DateTimeOffset.UtcNow);
}

/// <summary>Records every email; optionally slow (to make concurrent workers overlap) or failing (to exercise back-off).</summary>
public sealed class FakeEmailSender(bool fail = false, int delayMs = 0) : IEmailSender
{
    public ConcurrentBag<(string To, string Subject, string Body)> Sent { get; } = [];
    public int Calls => _calls;
    private int _calls;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, ct);
        }
        if (fail)
        {
            throw new InvalidOperationException("SMTP server unavailable (test)");
        }
        Sent.Add((to, subject, htmlBody));
    }
}

/// <summary>Test authentication: <c>X-Test-User: {guid}</c> becomes the "sub" claim, <c>X-Test-Roles: a,b</c> become "role" claims.</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        var claims = new List<Claim> { new("sub", user) };
        claims.AddRange(Request.Headers["X-Test-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim("role", r)));
        var identity = new ClaimsIdentity(claims, SchemeName, "sub", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
