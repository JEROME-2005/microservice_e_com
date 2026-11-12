using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using LankaMart.Orders.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Workers;

/// <summary>Saga timeout: an order still PENDING_PAYMENT after its window is EXPIRED and its stock released (System Scenarios §2.5).</summary>
public sealed class ExpireStaleOrdersWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExpireStaleOrdersWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await ExpireOnceAsync(scope.ServiceProvider, clock, log, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Expiry sweep failed");
            }
        }
    }

    /// <summary>One sweep (public so tests can run it with a fake clock instead of waiting 15 minutes).</summary>
    public static async Task<int> ExpireOnceAsync(IServiceProvider sp, TimeProvider clock, ILogger log, CancellationToken ct)
    {
        var db = sp.GetRequiredService<OrdersDb>();
        var bus = sp.GetRequiredService<IPublishEndpoint>();     // resolved from the SAME scope as the DbContext → its outbox rows land in this db
        var now = clock.GetUtcNow();
        var stale = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.PendingPayment && o.PaymentExpiresAt < now).Take(100).ToListAsync(ct);
        var expired = 0;
        foreach (var o in stale)
        {
            // transition + OrderCancelled in ONE transaction: an expired order always releases its stock
            var changed = await db.InTransactionAsync(async () =>
            {
                if (!await OrderTransitions.TryAsync(db, o.Id, OrderStatus.PendingPayment, OrderStatus.Expired, "system", "Payment window elapsed", now, "PAYMENT_TIMEOUT"))
                {
                    return false;
                }
                await bus.Publish(new OrderCancelled(o.Id, o.CustomerId, "PAYMENT_TIMEOUT", CheckoutService.ToLines(o), now), ct);
                return true;
            }, ct);
            if (!changed)
            {
                continue;
            }
            log.LogInformation("Order {OrderNumber} expired", o.OrderNumber);
            expired++;
        }
        return expired;
    }
}
