using Microsoft.EntityFrameworkCore;

namespace LankaMart.BuildingBlocks;

public static class TransactionExtensions
{
    /// <summary>Runs <paramref name="work"/> inside ONE database transaction, then <c>SaveChanges</c> (which also writes the outbox rows
    /// of any <c>Publish</c> made inside <paramref name="work"/>) and commits.
    /// <para>Use it whenever code mixes <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> or several <c>SaveChanges</c> (each commits on its own)
    /// with <c>Publish</c> — without one transaction they would commit separately from the outbox row: the dual-write problem again.</para>
    /// <para>Nested use is safe: inside an existing transaction (e.g. a consumer, where the MassTransit consumer outbox opened it)
    /// the work joins it and the OWNER of that transaction commits.</para></summary>
    public static async Task<T> InTransactionAsync<T>(this DbContext db, Func<Task<T>> work, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            var inner = await work();
            await db.SaveChangesAsync(ct);
            return inner;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work();                                   // an exception here → tx disposed without commit → ROLLBACK
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public static Task InTransactionAsync(this DbContext db, Func<Task> work, CancellationToken ct = default) =>
        db.InTransactionAsync<bool>(async () =>
        {
            await work();
            return true;
        }, ct);
}
