using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;

namespace LankaMart.Catalog.Api.Consumers;

/// <summary>Keeps the <c>known_sellers</c> read model in sync with the Users service. Idempotent and order-tolerant:
/// a duplicate delivery re-applies the same status, and a STALE event (older than what we have) is ignored.</summary>
public sealed class SellerStatusConsumer(CatalogDb db) :
    IConsumer<SellerApproved>, IConsumer<SellerRejected>, IConsumer<SellerSuspended>, IConsumer<SellerReinstated>
{
    public Task Consume(ConsumeContext<SellerApproved> context) => Apply(context.Message.SellerId, "Active", context.Message.OccurredAt);
    public Task Consume(ConsumeContext<SellerRejected> context) => Apply(context.Message.SellerId, "Rejected", context.Message.OccurredAt);
    public Task Consume(ConsumeContext<SellerSuspended> context) => Apply(context.Message.SellerId, "Suspended", context.Message.OccurredAt);
    public Task Consume(ConsumeContext<SellerReinstated> context) => Apply(context.Message.SellerId, "Active", context.Message.OccurredAt);

    public async Task Apply(Guid sellerId, string status, DateTimeOffset at)
    {
        var s = await db.KnownSellers.FindAsync(sellerId);
        if (s is null)
        {
            db.KnownSellers.Add(new KnownSeller { SellerId = sellerId, Status = status, UpdatedAt = at });
        }
        else if (at > s.UpdatedAt)       // ignore STALE events: messages can arrive out of order
        {
            s.Status = status;
            s.UpdatedAt = at;
        }
        await db.SaveChangesAsync();
    }
}
