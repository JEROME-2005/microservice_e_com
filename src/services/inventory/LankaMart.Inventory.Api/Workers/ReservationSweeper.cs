using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using LankaMart.Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Workers;

/// <summary>Safety net #1: reservations nobody confirmed or released are freed after their TTL. This is what stops abandoned carts from locking stock forever.</summary>
public sealed class ReservationSweeper(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReservationSweeper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<InventoryDb>();
                var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
                var now = clock.GetUtcNow();
                var expired = await db.Reservations.AsNoTracking().Where(r => r.Status == ReservationStatus.Reserved && r.ExpiresAt < now)
                    .Select(r => r.Id).Take(200).ToListAsync(stoppingToken);
                foreach (var id in expired)
                {
                    await svc.ReleaseAsync(id, stoppingToken);
                }
                if (expired.Count > 0)
                {
                    log.LogInformation("Sweeper released {Count} expired reservations", expired.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Sweeper iteration failed");   // a sweeper must never die
            }
        }
    }
}
