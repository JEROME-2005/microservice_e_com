using LankaMart.Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LankaMart.Inventory.Api.Stock;

/// <summary>The hot path: <c>stock:{variantId}</c> = available units (on-hand − reserved). Postgres stays the durable truth.</summary>
public sealed class RedisStock(IConnectionMultiplexer redis, InventoryDb db)
{
    private IDatabase R => redis.GetDatabase();
    public static string Key(Guid variantId) => $"stock:{variantId}";

    // Atomic multi-variant reserve. KEYS = stock keys; ARGV = quantities (same order).
    // returns: 1 = reserved · 0 = insufficient stock · -1 = a counter is missing (cache cold → load from Postgres and retry)
    private const string ReserveLua = """
        local n = #KEYS
        for i = 1, n do
          local s = redis.call('GET', KEYS[i])
          if s == false then return -1 end
          if tonumber(s) < tonumber(ARGV[i]) then return 0 end
        end
        for i = 1, n do redis.call('DECRBY', KEYS[i], ARGV[i]) end
        return 1
        """;

    /// <summary>Make sure counters exist. SET NX = "only if missing", so concurrent loaders can't overwrite a live counter.</summary>
    public async Task EnsureLoadedAsync(IEnumerable<Guid> variantIds, CancellationToken ct)
    {
        foreach (var id in variantIds.Distinct())
        {
            if (await R.KeyExistsAsync(Key(id)))
            {
                continue;
            }
            var avail = await db.Items.AsNoTracking().Where(i => i.VariantId == id).Select(i => (int?)(i.QtyOnHand - i.QtyReserved)).SingleOrDefaultAsync(ct);
            if (avail is not null)
            {
                await R.StringSetAsync(Key(id), avail.Value, when: When.NotExists);
            }
        }
    }

    /// <returns>1 ok · 0 insufficient · -1 missing counter</returns>
    public async Task<long> TryReserveAsync(IReadOnlyList<(Guid VariantId, int Qty)> lines)
    {
        var keys = lines.Select(l => (RedisKey)Key(l.VariantId)).ToArray();
        var args = lines.Select(l => (RedisValue)l.Qty).ToArray();
        return (long)await R.ScriptEvaluateAsync(ReserveLua, keys, args);
    }

    public async Task AddBackAsync(IEnumerable<(Guid VariantId, int Qty)> lines)     // release / compensation
    {
        foreach (var l in lines)
        {
            await R.StringIncrementAsync(Key(l.VariantId), l.Qty);
        }
    }

    // INCRBY only if the key exists (atomic): a cold counter must be loaded from Postgres instead, never created from a bare delta.
    private const string AdjustIfExistsLua = """
        if redis.call('EXISTS', KEYS[1]) == 1 then
          redis.call('INCRBY', KEYS[1], ARGV[1])
          return 1
        end
        return 0
        """;

    /// <summary>Restock / correction: keep a LIVE Redis counter in step with Postgres. Returns false if the counter was cold (nothing changed).</summary>
    public async Task<bool> AdjustAsync(Guid variantId, int delta) =>
        (long)await R.ScriptEvaluateAsync(AdjustIfExistsLua, [(RedisKey)Key(variantId)], [(RedisValue)delta]) == 1;

    public async Task<int?> GetAsync(Guid variantId)
    {
        var v = await R.StringGetAsync(Key(variantId));
        return v.HasValue && int.TryParse(v.ToString(), out var n) ? n : null;     // never throw on a non-numeric value
    }
}
