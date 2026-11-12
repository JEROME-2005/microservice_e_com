using LankaMart.Orders.Api.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LankaMart.Orders.Tests;

/// <summary>One throwaway Postgres 17 per test class (needs Docker), migrated with the real Orders migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("orders_db").Build();

    public string ConnectionString => _pg.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        var options = new DbContextOptionsBuilder<OrdersDb>()
            .UseNpgsql(ConnectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "orders"))
            .UseSnakeCaseNamingConvention().Options;
        await using var db = new OrdersDb(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();
}
