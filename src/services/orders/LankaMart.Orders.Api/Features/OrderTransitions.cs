using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Features;

public static class OrderTransitions
{
    /// <summary>Atomically move an order from <paramref name="from"/> to <paramref name="to"/> ONLY IF it is currently in <paramref name="from"/>.
    /// Returns false if someone else already changed it (e.g., payment succeeded while the expiry sweeper was running) — the caller then does nothing.
    /// This single pattern removes whole classes of race conditions without locks.
    /// <para>Transaction-aware (Phase 2): inside a consumer (MassTransit's consumer outbox owns the transaction) or an outer
    /// <c>db.InTransactionAsync(...)</c> it JOINS that transaction, so the transition and the event published next to it commit
    /// together. Called on its own it opens and commits its own transaction, as before.</para></summary>
    public static Task<bool> TryAsync(OrdersDb db, Guid orderId, OrderStatus from, OrderStatus to, string actor, string? note, DateTimeOffset now, string? cancelReason = null)
    {
        OrderStateMachine.EnsureAllowed(from, to);
        return db.InTransactionAsync(async () =>
        {
            var rows = await db.Orders.Where(o => o.Id == orderId && o.Status == from)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to)
                                          .SetProperty(o => o.UpdatedAt, now)
                                          .SetProperty(o => o.CancelReason, o => cancelReason ?? o.CancelReason));
            if (rows == 0)
            {
                return false;
            }
            await db.Shipments.Where(s => s.OrderId == orderId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, to));   // Phase 1: shipments mirror the order; Phase 4 makes them independent
            db.Events.Add(new OrderEvent { Id = Guid.CreateVersion7(), OrderId = orderId, FromStatus = from, ToStatus = to, Actor = actor, Note = note, At = now });
            return true;                                                     // InTransactionAsync saves the event row (and any outbox rows)
        });
    }
}
