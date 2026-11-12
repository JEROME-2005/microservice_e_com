using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using LankaMart.Inventory.Api.Stock;
using LankaMart.Inventory.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace LankaMart.Inventory.Tests;

/// <summary>Real Postgres 17 + Redis 7 (needs Docker), migrated with the real Inventory migrations.</summary>
public sealed class StoresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("inventory_db").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_pg.StartAsync(), _redis.StartAsync());
        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString() + ",allowAdmin=true");
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public InventoryDb NewDb() => new(new DbContextOptionsBuilder<InventoryDb>()
        .UseNpgsql(_pg.GetConnectionString(), n => n.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
        .UseSnakeCaseNamingConvention().Options);

    /// <summary>Clean slate per test: the reconciler scans the whole table.</summary>
    public async Task ResetAsync()
    {
        await using var db = NewDb();
        await db.Items.ExecuteDeleteAsync();
        await Redis.GetServer(Redis.GetEndPoints()[0]).FlushAllDatabasesAsync();
    }

    public async Task DisposeAsync()
    {
        Redis?.Dispose();
        await Task.WhenAll(_pg.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

public sealed class ReconcilerTests(StoresFixture stores) : IClassFixture<StoresFixture>, IAsyncLifetime
{
    private readonly ListLogger _log = new();

    public Task InitializeAsync() => stores.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private IDatabase R => stores.Redis.GetDatabase();

    private ReconcilerService NewReconciler(InventoryDb db, bool autoHeal = true) => new(db, stores.Redis,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reconciler:AutoHeal"] = autoHeal ? "true" : "false",
            ["Reconciler:QuietSeconds"] = "60",
            ["Reconciler:RecheckDelayMs"] = "50",
        }).Build(), TimeProvider.System, _log);

    /// <summary>A variant with <paramref name="onHand"/> − <paramref name="reserved"/> available in Postgres and (optionally) a live Redis counter.</summary>
    private async Task<Guid> AddItemAsync(int onHand, int reserved = 0, long? redisValue = null, bool recent = false)
    {
        var id = Guid.NewGuid();
        await using (var db = stores.NewDb())
        {
            db.Items.Add(new InventoryItem
            {
                VariantId = id, SellerId = Guid.NewGuid(), Sku = $"T-{id:N}"[..12], QtyOnHand = onHand, QtyReserved = reserved,
                UpdatedAt = recent ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        }
        if (redisValue is not null)
        {
            await R.StringSetAsync(RedisStock.Key(id), redisValue.Value);
        }
        return id;
    }

    [Fact]
    public async Task Drifted_counter_is_detected_and_healed_toward_postgres()
    {
        var id = await AddItemAsync(onHand: 60, reserved: 10, redisValue: 7);          // Postgres says 50 available, Redis says 7

        await using var db = stores.NewDb();
        var report = await NewReconciler(db).RunAsync(CancellationToken.None);

        Assert.Equal(new ReconcileReport(1, 1, 1, 100), report);
        Assert.Equal(50, (long)await R.StringGetAsync(RedisStock.Key(id)));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("STOCK DRIFT"));
    }

    [Fact]
    public async Task Crash_leak_after_redis_decrement_is_healed()
    {
        var id = await AddItemAsync(onHand: 50, redisValue: 47);                       // DECRBY 3 happened, the Postgres write never did

        await using var db = stores.NewDb();
        Assert.Equal(1, (await NewReconciler(db).RunAsync(CancellationToken.None)).Healed);
        Assert.Equal(50, (long)await R.StringGetAsync(RedisStock.Key(id)));
    }

    [Fact]
    public async Task Compare_and_set_never_clobbers_a_value_that_moved_meanwhile()
    {
        var id = await AddItemAsync(onHand: 50, redisValue: 7);
        var key = RedisStock.Key(id);
        await using var db = stores.NewDb();
        var reconciler = NewReconciler(db);

        await R.StringSetAsync(key, 6);                                                // a customer reserved 1 unit after we observed 7
        Assert.False(await reconciler.TryHealAsync(key, expected: 7, value: 50));
        Assert.Equal(6, (long)await R.StringGetAsync(key));                            // untouched

        Assert.True(await reconciler.TryHealAsync(key, expected: 6, value: 50));
        Assert.Equal(50, (long)await R.StringGetAsync(key));
    }

    [Fact]
    public async Task Recently_changed_rows_are_skipped_they_may_be_in_flight()
    {
        var id = await AddItemAsync(onHand: 50, redisValue: 49, recent: true);

        await using var db = stores.NewDb();
        var report = await NewReconciler(db).RunAsync(CancellationToken.None);

        Assert.Equal(0, report.Checked);
        Assert.Equal(49, (long)await R.StringGetAsync(RedisStock.Key(id)));
    }

    [Fact]
    public async Task Missing_counter_is_a_cold_cache_not_drift()
    {
        var id = await AddItemAsync(onHand: 50, redisValue: null);

        await using var db = stores.NewDb();
        var report = await NewReconciler(db).RunAsync(CancellationToken.None);

        Assert.Equal(new ReconcileReport(1, 0, 0, 0), report);
        Assert.False(await R.KeyExistsAsync(RedisStock.Key(id)));                      // lazily reloaded by the next reservation, not here
    }

    [Fact]
    public async Task Detect_only_mode_reports_but_does_not_heal()
    {
        var id = await AddItemAsync(onHand: 50, redisValue: 7);

        await using var db = stores.NewDb();
        var report = await NewReconciler(db, autoHeal: false).RunAsync(CancellationToken.None);

        Assert.Equal((1, 0), (report.Drifted, report.Healed));
        Assert.Equal(7, (long)await R.StringGetAsync(RedisStock.Key(id)));
    }

    [Fact]
    public async Task One_drifted_item_in_100_crosses_the_alert_threshold()
    {
        for (var i = 0; i < 99; i++)
        {
            await AddItemAsync(onHand: 20, redisValue: 20);
        }
        await AddItemAsync(onHand: 20, redisValue: 19);

        await using var db = stores.NewDb();
        var report = await NewReconciler(db).RunAsync(CancellationToken.None);

        Assert.Equal(100, report.Checked);
        Assert.Equal(1.0, report.DriftPercent, precision: 3);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("alert threshold"));
    }

    [Fact]
    public async Task A_healthy_system_reports_zero_drift()
    {
        for (var i = 0; i < 10; i++)
        {
            await AddItemAsync(onHand: 30, reserved: i, redisValue: 30 - i);
        }

        await using var db = stores.NewDb();
        var report = await NewReconciler(db).RunAsync(CancellationToken.None);

        Assert.Equal(new ReconcileReport(10, 0, 0, 0), report);
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }
}

public sealed class ListLogger : ILogger<ReconcilerService>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
