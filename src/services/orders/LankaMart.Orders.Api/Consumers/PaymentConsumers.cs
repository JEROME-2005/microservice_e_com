using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using LankaMart.Orders.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Consumers;

public sealed class PaymentCompletedConsumer(OrdersDb db, IPublishEndpoint bus, TimeProvider clock) : IConsumer<PaymentCompleted>
{
    public async Task Consume(ConsumeContext<PaymentCompleted> context)
    {
        var m = context.Message;
        var now = clock.GetUtcNow();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == m.OrderId);
        if (order is null)
        {
            return;                                                          // unknown order → nothing to do (logged by MassTransit)
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            if (order.Status is not (OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.PaymentFailed))
            {
                return;   // already Paid or further along → duplicate delivery → idempotent no-op
            }
            // Paid AFTER we expired/cancelled the order: the customer's money is taken but there is no order. Tell Payments → refund flow (Phase 5).
            await bus.Publish(new OrderPaymentOrphaned(order.Id, m.PaymentId, now));
            return;
        }
        if (!await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, OrderStatus.Paid, "payment", $"Payment {m.GatewayRef}", now))
        {
            return;   // lost a race → someone else handled it
        }
        await bus.Publish(new OrderPaid(order.Id, order.OrderNumber, order.CustomerId, order.TotalCents, CheckoutService.ToLines(order), now));            // Inventory confirms stock
        await bus.Publish(new OrderStatusChanged(order.Id, order.CustomerId, nameof(OrderStatus.PendingPayment), nameof(OrderStatus.Paid), now));
    }
}

public sealed class PaymentFailedConsumer(OrdersDb db, IPublishEndpoint bus, CartService carts, TimeProvider clock) : IConsumer<PaymentFailed>
{
    public async Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var m = context.Message;
        var now = clock.GetUtcNow();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == m.OrderId);
        if (order is null)
        {
            return;
        }
        // COMPENSATION: PENDING_PAYMENT → PAYMENT_FAILED, release stock, give the customer their cart back so they can retry
        if (!await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, OrderStatus.PaymentFailed, "payment", m.FailureCode, now, m.FailureCode))
        {
            return;
        }
        await bus.Publish(new OrderCancelled(order.Id, order.CustomerId, "PAYMENT_FAILED:" + m.FailureCode, CheckoutService.ToLines(order), now));
        await carts.RestoreAsync(order.CustomerId, order.Items.Select(i => (i.VariantId, i.Quantity)));
        await bus.Publish(new OrderStatusChanged(order.Id, order.CustomerId, nameof(OrderStatus.PendingPayment), nameof(OrderStatus.PaymentFailed), now));
    }
}

public sealed class ReservationConfirmFailedConsumer(OrdersDb db, TimeProvider clock, ILogger<ReservationConfirmFailedConsumer> log) : IConsumer<ReservationConfirmFailed>
{
    public async Task Consume(ConsumeContext<ReservationConfirmFailed> context)
    {
        // Paid but stock could not be confirmed (oversold). System Scenarios §2.5: "auto-refund + apology". The refund itself is built in Phase 5.
        log.LogError("OVERSOLD order {OrderId}: {Reason} — needs refund", context.Message.OrderId, context.Message.Reason);
        await OrderTransitions.TryAsync(db, context.Message.OrderId, OrderStatus.Paid, OrderStatus.RefundPending, "system", context.Message.Reason, clock.GetUtcNow());
    }
}
