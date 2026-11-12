using FluentValidation;
using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using LankaMart.Inventory.Api.Stock;
using LankaMart.Inventory.Api.Workers;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Features;

public sealed record AdjustStockRequest(Guid VariantId, int Delta, string Reason);

public sealed class AdjustValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustValidator()
    {
        RuleFor(x => x.VariantId).NotEmpty();
        RuleFor(x => x.Delta).NotEqual(0).InclusiveBetween(-100000, 100000);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);   // reason mandatory → audit trail
    }
}

public static class Endpoints
{
    public static void MapInventory(this WebApplication app)
    {
        // PUBLIC: coarse availability only (never exact counts — competitors would scrape them)
        app.MapGet("/api/v1/inventory/availability", async (string? variantIds, RedisStock redis, CancellationToken ct) =>
        {
            var ids = (variantIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                                .Where(g => g != Guid.Empty).Distinct().Take(50).ToList();
            await redis.EnsureLoadedAsync(ids, ct);
            var rows = new List<object>();
            foreach (var id in ids)
            {
                var a = await redis.GetAsync(id) ?? 0;
                rows.Add(new { variantId = id, status = a <= 0 ? "out_of_stock" : a <= 5 ? "low_stock" : "in_stock" });
            }
            return Results.Ok(rows);
        }).AllowAnonymous();

        // SELLER
        var s = app.MapGroup("/api/v1/seller/inventory").WithTags("Seller inventory").RequireAuthorization(Policies.Seller);
        s.MapGet("", async (HttpContext c, InventoryDb db, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            return Results.Ok(await db.Items.AsNoTracking().Where(i => i.SellerId == sid).OrderBy(i => i.Sku)
                .Select(i => new { i.VariantId, i.Sku, i.QtyOnHand, i.QtyReserved, available = i.QtyOnHand - i.QtyReserved, i.ReorderPoint }).ToListAsync(ct));
        });

        s.MapPost("/adjust", async (AdjustStockRequest r, HttpContext c, InventoryDb db, RedisStock redis, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var now = clock.GetUtcNow();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Items.Where(i => i.VariantId == r.VariantId && i.SellerId == sid && i.QtyOnHand + r.Delta >= i.QtyReserved)   // ownership AND no-negative-available in one statement
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.QtyOnHand, i => i.QtyOnHand + r.Delta).SetProperty(i => i.UpdatedAt, now), ct);
            if (rows == 0)
            {
                throw await db.Items.AnyAsync(i => i.VariantId == r.VariantId && i.SellerId == sid, ct)
                    ? new BusinessRuleException("WOULD_GO_NEGATIVE", "That adjustment would drop stock below what is already reserved.")
                    : new NotFoundException();                                                         // not yours / doesn't exist → 404 (no existence leak)
            }
            db.Movements.Add(new StockMovement { Id = Guid.CreateVersion7(), VariantId = r.VariantId, Delta = r.Delta, Reason = r.Reason, ActorId = c.User.UserId(), At = now });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            // Live counter → shift it by the delta. Cold counter → load it from Postgres, which ALREADY includes the delta.
            // (The guide's "EnsureLoaded, then Adjust" would count the delta twice on a cold counter.)
            if (!await redis.AdjustAsync(r.VariantId, r.Delta))
            {
                await redis.EnsureLoadedAsync([r.VariantId], ct);
            }
            return Results.NoContent();
        }).WithValidation<AdjustStockRequest>();

        // INTERNAL (Orders → Inventory)
        var i = app.MapInternalGroup("/api/v1/internal/inventory");
        i.MapPost("/reserve", async (ReserveStockRequest r, ReservationService svc, CancellationToken ct) => Results.Ok(await svc.ReserveAsync(r, ct)));
        i.MapPost("/confirm/{reservationId:guid}", async (Guid reservationId, ReservationService svc, CancellationToken ct) =>
        {
            await svc.ConfirmAsync(reservationId, ct);
            return Results.NoContent();
        });
        i.MapPost("/release/{reservationId:guid}", async (Guid reservationId, ReservationService svc, CancellationToken ct) =>
        {
            await svc.ReleaseAsync(reservationId, ct);
            return Results.NoContent();
        });
        // On-demand stock reconciliation (ops / demo): compares every quiet Redis counter with Postgres, heals drift with compare-and-set
        i.MapPost("/reconcile", async (ReconcilerService r, CancellationToken ct) => Results.Ok(await r.RunAsync(ct)));
    }
}
