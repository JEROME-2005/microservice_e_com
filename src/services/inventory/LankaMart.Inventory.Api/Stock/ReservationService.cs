using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LankaMart.Inventory.Api.Stock;

public sealed class ReservationService(InventoryDb db, RedisStock redis, IPublishEndpoint bus, TimeProvider clock, ILogger<ReservationService> log)
{
    public async Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest req, CancellationToken ct)
    {
        // 1) IDEMPOTENT: Orders retries on timeouts (resilience handler). Same reservation id → same answer, no double-decrement.
        if (await db.Reservations.AnyAsync(r => r.Id == req.ReservationId, ct))
        {
            return new(true, []);
        }

        var lines = (req.Lines ?? []).GroupBy(l => l.VariantId).Select(g => (VariantId: g.Key, Qty: g.Sum(x => x.Quantity))).ToList();
        if (lines.Count == 0 || lines.Count > 50 || lines.Any(l => l.Qty <= 0 || l.Qty > 100))
        {
            throw new BusinessRuleException("INVALID_QUANTITY", "Quantity must be 1–100 per variant (max 50 variants).");
        }
        var ids = lines.Select(l => l.VariantId).ToList();

        // unknown variants (no inventory row) = unavailable
        var known = await db.Items.AsNoTracking().Where(i => ids.Contains(i.VariantId)).Select(i => i.VariantId).ToListAsync(ct);
        var unknown = ids.Except(known).ToList();
        if (unknown.Count > 0)
        {
            return new(false, unknown);
        }

        // 2) Redis atomic check-and-decrement (cold counter? load from Postgres once, retry)
        await redis.EnsureLoadedAsync(ids, ct);
        var code = await redis.TryReserveAsync(lines);
        if (code == -1)
        {
            await redis.EnsureLoadedAsync(ids, ct);
            code = await redis.TryReserveAsync(lines);
        }
        if (code != 1)
        {
            return new(false, await FindShortfallAsync(lines));
        }

        // 3) Persist. If Postgres refuses (e.g., drift: Redis said yes, DB says no), undo the Redis decrement → never leak stock.
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            foreach (var (variantId, qty) in lines)
            {
                var rows = await db.Items
                    .Where(i => i.VariantId == variantId && i.QtyOnHand - i.QtyReserved >= qty)       // conditional UPDATE = compare-and-set
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.QtyReserved, i => i.QtyReserved + qty).SetProperty(i => i.UpdatedAt, now), ct);
                if (rows == 0)
                {
                    await tx.RollbackAsync(ct);
                    await redis.AddBackAsync(lines);
                    return new(false, [variantId]);
                }
            }
            var res = new Reservation { Id = req.ReservationId, Status = ReservationStatus.Reserved, CreatedAt = now, ExpiresAt = now.AddSeconds(Math.Clamp(req.TtlSeconds, 60, 3600)) };
            foreach (var (variantId, qty) in lines)
            {
                res.Lines.Add(new ReservationLine { ReservationId = res.Id, VariantId = variantId, Quantity = qty });
            }
            db.Reservations.Add(res);
            await PublishLowStockAsync(ids, ct);          // outbox rows (reads the UPDATED quantities inside this transaction) …
            await db.SaveChangesAsync(ct);                // … committed together with the reservation: no lost alert, no alert for a rolled-back reservation
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // the SAME reservation id raced us and won → our decrement was a duplicate; give it back and report the winner's success
            await redis.AddBackAsync(lines);
            return new(true, []);
        }
        catch
        {
            await redis.AddBackAsync(lines);
            throw;
        }
        // A crash between steps 2 and 3 leaks Redis stock; the StockReconciler (Workers/StockReconciler.cs) detects and heals that drift.
        return new(true, []);
    }

    /// <summary>Called when OrderPaid arrives (consumer) or via the internal endpoint. Idempotent: only a RESERVED reservation transitions.
    /// Every Publish is followed by SaveChanges on the same DbContext — inside a consumer that joins MassTransit's transaction,
    /// from the HTTP endpoint it commits the outbox row itself.</summary>
    public async Task ConfirmAsync(Guid reservationId, CancellationToken ct)
    {
        var res = await db.Reservations.AsNoTracking().Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == reservationId, ct);
        if (res is null)
        {
            await bus.Publish(new ReservationConfirmFailed(reservationId, "RESERVATION_NOT_FOUND", clock.GetUtcNow()), ct);
            await db.SaveChangesAsync(ct);
            return;
        }
        if (res.Status == ReservationStatus.Confirmed)
        {
            return;                                           // duplicate delivery → no-op
        }
        if (res.Status == ReservationStatus.Released)         // swept before payment arrived (race)
        {
            // The TTL is longer than the payment window so this is rare. If stock is gone, Orders must refund (Phase 5).
            await bus.Publish(new ReservationConfirmFailed(reservationId, "RESERVATION_EXPIRED", clock.GetUtcNow()), ct);
            await db.SaveChangesAsync(ct);
            return;
        }
        var now = clock.GetUtcNow();
        // InTransactionAsync, not BeginTransaction: inside a consumer MassTransit's consumer outbox already owns the transaction
        // (a second BeginTransaction would throw); from the internal endpoint it opens and commits its own.
        await db.InTransactionAsync(async () =>
        {
            var flipped = await db.Reservations.Where(r => r.Id == reservationId && r.Status == ReservationStatus.Reserved)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Confirmed), ct);    // atomic claim
            if (flipped == 0)
            {
                return;
            }
            foreach (var l in res.Lines)
            {
                await db.Items.Where(i => i.VariantId == l.VariantId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.QtyOnHand, i => i.QtyOnHand - l.Quantity)
                                              .SetProperty(i => i.QtyReserved, i => i.QtyReserved - l.Quantity)
                                              .SetProperty(i => i.UpdatedAt, now), ct);
            }
        }, ct);
        // Redis counter already reflects the sale (decremented at reserve time) → nothing to do there.
    }

    /// <summary>Called on OrderCancelled and by the sweeper. Idempotent via the atomic status flip.</summary>
    public async Task ReleaseAsync(Guid reservationId, CancellationToken ct)
    {
        var res = await db.Reservations.AsNoTracking().Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == reservationId, ct);
        if (res is null)
        {
            return;
        }
        var now = clock.GetUtcNow();
        var released = await db.InTransactionAsync(async () =>      // joins the consumer's transaction when called from OrderCancelledConsumer
        {
            var flipped = await db.Reservations.Where(r => r.Id == reservationId && r.Status == ReservationStatus.Reserved)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Released), ct);
            if (flipped == 0)
            {
                return false;                             // already confirmed/released → someone else won the race
            }
            foreach (var l in res.Lines)
            {
                await db.Items.Where(i => i.VariantId == l.VariantId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.QtyReserved, i => i.QtyReserved - l.Quantity).SetProperty(i => i.UpdatedAt, now), ct);
            }
            return true;
        }, ct);
        if (!released)
        {
            return;
        }
        // Redis is not part of the DB transaction. Inside a consumer the commit happens after this method returns; in the rare case
        // that commit then fails, the retry adds the units back a second time — the StockReconciler detects and heals that drift.
        await redis.AddBackAsync(res.Lines.Select(l => (l.VariantId, l.Quantity)));
        log.LogInformation("Released reservation {ReservationId}", reservationId);
    }

    private async Task<List<Guid>> FindShortfallAsync(List<(Guid VariantId, int Qty)> lines)
    {
        var bad = new List<Guid>();
        foreach (var l in lines)
        {
            if ((await redis.GetAsync(l.VariantId) ?? 0) < l.Qty)
            {
                bad.Add(l.VariantId);
            }
        }
        return bad;
    }

    private async Task PublishLowStockAsync(List<Guid> ids, CancellationToken ct)
    {
        var low = await db.Items.AsNoTracking().Where(i => ids.Contains(i.VariantId) && i.QtyOnHand - i.QtyReserved <= i.ReorderPoint).ToListAsync(ct);
        foreach (var i in low)
        {
            await bus.Publish(new InventoryLowStock(i.VariantId, i.SellerId, i.Available, i.ReorderPoint, clock.GetUtcNow()), ct);
        }
    }
}
