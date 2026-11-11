using LankaMart.Orders.Api.Clients;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LankaMart.Orders.Api.Features;

public sealed class OrdersOptions
{
    public int PaymentWindowMinutes { get; set; } = 15;
    public int ReservationTtlMinutes { get; set; } = 17;      // longer than the payment window: a last-second payment still finds its stock
    public long DeliveryFeePerShipmentCents { get; set; } = 35000;
    public int MaxQtyPerItem { get; set; } = 10;
}

public sealed record CartLineDto(Guid VariantId, int Quantity, string? Name, string? ImageUrl, long UnitPriceCents, long LineTotalCents, bool Available);
public sealed record CartDto(IReadOnlyList<CartLineDto> Lines, long SubtotalCents);

/// <summary>Cart in Redis: a hash per cart (<c>cart:u:{userId}</c> / <c>cart:g:{guestId}</c>), field = variantId, value = quantity.
/// Only ids and quantities — NEVER prices; prices come fresh from Catalog every time the cart is shown or ordered.</summary>
public sealed class CartService(IConnectionMultiplexer redis, ICatalogClient catalog, IOptions<OrdersOptions> opt)
{
    public const int MaxLines = 50;
    private IDatabase Db => redis.GetDatabase();
    public static string UserKey(Guid id) => $"cart:u:{id}";
    public static string GuestKey(Guid id) => $"cart:g:{id}";

    /// <summary>Who owns this cart? Logged-in → by token. Guest → by X-Guest-Id header (a random GUID the client invents and keeps in localStorage).</summary>
    public static string KeyFor(HttpContext c)
    {
        if (c.User.TryUserId() is { } uid)
        {
            return UserKey(uid);
        }
        if (Guid.TryParse(c.Request.Headers["X-Guest-Id"].FirstOrDefault(), out var gid) && gid != Guid.Empty)
        {
            return GuestKey(gid);
        }
        throw new BusinessRuleException("GUEST_ID_REQUIRED", "Send an X-Guest-Id header (a GUID) or sign in.");
    }

    /// <param name="add">true = add <paramref name="qty"/> to what is there (capped at the max); false = set the quantity exactly.</param>
    public async Task SetAsync(string key, Guid variantId, int qty, bool add)
    {
        var max = opt.Value.MaxQtyPerItem;
        if (qty < 0 || qty > max || (add && qty == 0))
        {
            throw new BusinessRuleException("INVALID_QUANTITY", $"Quantity must be {(add ? 1 : 0)}–{max}.");
        }
        var field = variantId.ToString();
        if (qty == 0)
        {
            await Db.HashDeleteAsync(key, field);
            return;
        }
        if (await Db.HashLengthAsync(key) >= MaxLines && !await Db.HashExistsAsync(key, field))
        {
            throw new BusinessRuleException("CART_FULL", $"Cart can hold up to {MaxLines} different items.");
        }
        if (add)
        {
            var newQty = await Db.HashIncrementAsync(key, field, qty);
            if (newQty > max)
            {
                await Db.HashSetAsync(key, field, max);              // adding more than the max keeps the line at the max
            }
        }
        else
        {
            await Db.HashSetAsync(key, field, qty);
        }
        await Db.KeyExpireAsync(key, key.StartsWith("cart:g:", StringComparison.Ordinal) ? TimeSpan.FromDays(30) : TimeSpan.FromDays(90));
    }

    public async Task<Dictionary<Guid, int>> RawAsync(string key) =>
        (await Db.HashGetAllAsync(key)).ToDictionary(e => Guid.Parse(e.Name.ToString()), e => (int)e.Value);

    public async Task ClearAsync(string key) => await Db.KeyDeleteAsync(key);

    /// <summary>After login: fold the guest cart into the user's cart (max quantity wins), then delete the guest cart.</summary>
    public async Task MergeAsync(Guid guestId, Guid userId)
    {
        var guest = await RawAsync(GuestKey(guestId));
        var userKey = UserKey(userId);
        foreach (var (variantId, qty) in guest)
        {
            var field = variantId.ToString();
            var exists = await Db.HashExistsAsync(userKey, field);
            if (!exists && await Db.HashLengthAsync(userKey) >= MaxLines)
            {
                continue;                                             // never exceed the line cap through a merge
            }
            var current = (int?)await Db.HashGetAsync(userKey, field) ?? 0;
            await Db.HashSetAsync(userKey, field, Math.Min(Math.Max(current, qty), opt.Value.MaxQtyPerItem));
        }
        if (guest.Count > 0)
        {
            await Db.KeyExpireAsync(userKey, TimeSpan.FromDays(90));
        }
        await Db.KeyDeleteAsync(GuestKey(guestId));
    }

    public async Task<CartDto> ViewAsync(string key, CancellationToken ct)
    {
        var raw = await RawAsync(key);
        if (raw.Count == 0)
        {
            return new([], 0);
        }
        var info = (await catalog.LookupAsync(raw.Keys, ct)).ToDictionary(v => v.VariantId);
        var lines = raw.Select(kv =>
        {
            info.TryGetValue(kv.Key, out var v);
            var price = v?.PriceCents ?? 0;
            return new CartLineDto(kv.Key, kv.Value, v?.ProductName, v?.ImageUrl, price, price * kv.Value, v?.IsPurchasable ?? false);
        }).ToList();
        return new(lines, lines.Where(l => l.Available).Sum(l => l.LineTotalCents));
    }

    /// <summary>Used when a payment fails: put the items back so the customer can retry.</summary>
    public async Task RestoreAsync(Guid userId, IEnumerable<(Guid VariantId, int Qty)> lines)
    {
        foreach (var (v, q) in lines)
        {
            await Db.HashSetAsync(UserKey(userId), v.ToString(), Math.Min(q, opt.Value.MaxQtyPerItem));
        }
        await Db.KeyExpireAsync(UserKey(userId), TimeSpan.FromDays(90));
    }
}
