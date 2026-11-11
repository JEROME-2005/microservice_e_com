using LankaMart.Inventory.Api.Data;
using LankaMart.Inventory.Api.Domain;
using LankaMart.Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Inventory.Api.Consumers;

/// <summary>A variant was created in Catalog → create its stock row (qty 0, or a demo quantity in Development).</summary>
public sealed class VariantCreatedConsumer(InventoryDb db, IConfiguration cfg) : IConsumer<VariantCreated>
{
    public async Task Consume(ConsumeContext<VariantCreated> context)
    {
        var m = context.Message;
        if (await db.Items.AnyAsync(i => i.VariantId == m.VariantId))
        {
            return;                 // idempotent
        }
        db.Items.Add(new InventoryItem { VariantId = m.VariantId, SellerId = m.SellerId, Sku = m.Sku, QtyOnHand = cfg.GetValue("Inventory:DevDefaultStock", 0), UpdatedAt = m.OccurredAt });
        await db.SaveChangesAsync();
    }
}

public sealed class OrderPaidConsumer(ReservationService svc) : IConsumer<OrderPaid>
{
    public Task Consume(ConsumeContext<OrderPaid> context) => svc.ConfirmAsync(context.Message.OrderId, context.CancellationToken);
}

public sealed class OrderCancelledConsumer(ReservationService svc) : IConsumer<OrderCancelled>
{
    public Task Consume(ConsumeContext<OrderCancelled> context) => svc.ReleaseAsync(context.Message.OrderId, context.CancellationToken);
}
