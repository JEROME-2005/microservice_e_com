using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LankaMart.BuildingBlocks;

public static class MigrationExtensions
{
    public const string MigrateOnlyArg = "--migrate-only";

    /// <summary>Call right after <c>Build()</c>. Returns true when the process should EXIT (job mode).
    /// <list type="bullet">
    /// <item>Development: auto-migrates at start (convenient).</item>
    /// <item>Any environment: <c>dotnet X.dll --migrate-only</c> migrates, then the caller returns → exit code 0
    ///       (a failure after 10 attempts throws → non-zero exit → the Kubernetes/Argo Job fails loudly and the rollout stops).</item>
    /// <item>Elsewhere: never migrates at start (<c>Database:AutoMigrate</c> defaults to false), so N replicas can't race.</item>
    /// </list></summary>
    public static async Task<bool> RunMigrationsIfRequestedAsync<TContext>(this WebApplication app, string[] args) where TContext : DbContext
    {
        var migrateOnly = args.Contains(MigrateOnlyArg);
        var auto = app.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment());
        if (migrateOnly || auto)
        {
            await app.MigrateDatabaseAsync<TContext>();
        }
        return migrateOnly;
    }

    /// <summary>Applies pending migrations, retrying while the DB container is still starting (10 attempts, 3 s apart).</summary>
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
