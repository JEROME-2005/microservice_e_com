using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LankaMart.Inventory.Api.Workers;

public sealed record ReconcileReport(int Checked, int Drifted, int Healed, double DriftPercent);

/// <summary>Redis↔Postgres stock drift detector and healer (known gap #6, docs/adr/0022-stock-reconciler.md).
/// Postgres is the ACCOUNTING truth, Redis the OPERATIONAL one: compare <c>stock:{variant}</c> with <c>qty_on_hand − qty_reserved</c> and
/// correct Redis toward Postgres. Drift happens when the process dies between "Redis decrement" and "Postgres insert" (stock leaked, Redis
/// too low) or Redis restarts from an old snapshot (Redis too high → oversell risk).
/// <para>Two safety rules for a healer that edits live data: (1) DOUBLE-CHECK — an in-flight reservation looks like drift for a few
/// milliseconds, so rows touched in the last minute are skipped and a suspect is re-read after a pause; (2) COMPARE-AND-SET — Redis is
/// only overwritten if it STILL holds the drifted value we observed, so a customer reserving at that instant is never clobbered.</para></summary>
public sealed class ReconcilerService(InventoryDb db, IConnectionMultiplexer redis, IConfiguration cfg, TimeProvider clock, ILogger<ReconcilerService> log)
{
    public const double AlertThresholdPercent = 0.5;                  // architecture doc §4.4: alert on drift > 0.5 %
    private const int BatchSize = 500;

    // "heal only if Redis still holds the value I saw" — atomic inside Redis
    private const string CasLua = "if redis.call('GET', KEYS[1]) == ARGV[1] then redis.call('SET', KEYS[1], ARGV[2]) return 1 end return 0";

    public async Task<ReconcileReport> RunAsync(CancellationToken ct)
    {
        var autoHeal = cfg.GetValue("Reconciler:AutoHeal", true);
        var quietSince = clock.GetUtcNow().AddSeconds(-cfg.GetValue("Reconciler:QuietSeconds", 60));   // skip items changed in the last minute
        var recheckDelay = TimeSpan.FromMilliseconds(cfg.GetValue("Reconciler:RecheckDelayMs", 500));
        var r = redis.GetDatabase();
        int checkedN = 0, drifted = 0, healed = 0;

        for (var offset = 0; ; offset += BatchSize)
        {
            var batch = await db.Items.AsNoTracking().Where(i => i.UpdatedAt < quietSince).OrderBy(i => i.VariantId).Skip(offset).Take(BatchSize)
                .Select(i => new { i.VariantId, Avail = i.QtyOnHand - i.QtyReserved }).ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }
            var seen = await r.StringGetAsync(batch.Select(i => (RedisKey)RedisStock.Key(i.VariantId)).ToArray());   // one MGET per batch
            for (var n = 0; n < batch.Count; n++)
            {
                var item = batch[n];
                checkedN++;
                if (!seen[n].HasValue || (long)seen[n] == item.Avail)
                {
                    continue;                                          // missing counter = cold cache (lazily reloaded), not drift
                }

                await Task.Delay(recheckDelay, ct);                    // double-check: let any in-flight reservation finish
                var key = RedisStock.Key(item.VariantId);
                var fresh = await db.Items.AsNoTracking().Where(i => i.VariantId == item.VariantId).Select(i => i.QtyOnHand - i.QtyReserved).SingleAsync(ct);
                var now = await r.StringGetAsync(key);
                if (!now.HasValue || (long)now == fresh)
                {
                    continue;
                }

                drifted++;
                log.LogWarning("STOCK DRIFT variant {Variant}: redis={Redis} postgres={Pg}", item.VariantId, (long)now, fresh);
                if (autoHeal && await TryHealAsync(key, (long)now, fresh))
                {
                    healed++;
                }
            }
        }

        var pct = checkedN == 0 ? 0 : 100.0 * drifted / checkedN;
        if (pct > AlertThresholdPercent)
        {
            log.LogError("STOCK DRIFT {Pct:F2}% of {Checked} items — above the {Threshold}% alert threshold, investigate the cause",
                pct, checkedN, AlertThresholdPercent);
        }
        else
        {
            log.LogInformation("Reconciler: checked {Checked}, drifted {Drifted}, healed {Healed}", checkedN, drifted, healed);
        }
        return new(checkedN, drifted, healed, pct);
    }

    /// <summary>Compare-and-set: <paramref name="expected"/> → <paramref name="value"/>, or nothing if Redis moved meanwhile.</summary>
    public async Task<bool> TryHealAsync(string key, long expected, long value) =>
        (long)await redis.GetDatabase().ScriptEvaluateAsync(CasLua, [(RedisKey)key], [(RedisValue)expected, (RedisValue)value]) == 1;
}

/// <summary>Runs the reconciler on start and then every <c>Reconciler:EveryMinutes</c> (default 60). Registered only when <c>Workers:Enabled</c>.</summary>
public sealed class StockReconciler(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<StockReconciler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(cfg.GetValue("Reconciler:EveryMinutes", 60)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReconcilerService>().RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Reconciler run failed");             // a worker must never die
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
