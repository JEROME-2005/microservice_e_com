using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LankaMart.BuildingBlocks;

public static class MigrationExtensions
{
    /// <summary>Applies pending migrations, retrying while the DB container is still starting. In production use a separate migration job (Phase 2).</summary>
    public static async Task MigrateDatabaseAsync<TContext>(this WebApplication app) where TContext : DbContext
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                log.LogInformation("Database is up to date");
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                log.LogWarning("DB migrate attempt {Attempt}/10 failed: {Message}. Retrying in 3s…", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
