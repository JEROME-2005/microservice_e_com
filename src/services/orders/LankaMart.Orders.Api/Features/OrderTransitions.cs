using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Features;

public static class OrderTransitions
{
    /// <summary>Atomically move an order from <paramref name="from"/> to <paramref name="to"/> ONLY IF it is currently in <paramref name="from"/>.
    /// Returns false if someone else already changed it (e.g., payment succeeded while the expiry sweeper was running) — the caller then does nothing.
    /// This single pattern removes whole classes of race conditions without locks.</summary>
    public static async Task<bool> TryAsync(OrdersDb db, Guid orderId, OrderStatus from, OrderStatus to, string actor, string? note, DateTimeOffset now, string? cancelReason = null)
    {
        OrderStateMachine.EnsureAllowed(from, to);
        await using var tx = await db.Database.BeginTransactionAsync();
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
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return true;
    }
}
