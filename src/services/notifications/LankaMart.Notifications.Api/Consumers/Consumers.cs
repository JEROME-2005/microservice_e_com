using System.Globalization;
using LankaMart.Notifications.Api.Data;
using LankaMart.Notifications.Api.Domain;
using LankaMart.Notifications.Api.Features;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Notifications.Api.Consumers;

// Every consumer runs under the consumer outbox + inbox (AddPlatformMessaging<NotificationsDb>): the contact/inbox/message rows and the
// processed-message record commit together, and the event's message id is the dedupe key → one event never notifies twice.

internal static class N
{
    public static string Lkr(long cents) => $"LKR {(cents / 100m).ToString("N2", CultureInfo.InvariantCulture)}";
    public static string? Id(ConsumeContext c) => c.MessageId?.ToString();
    public static string Humanize(string code) => code.Replace('_', ' ').Replace(':', ' ').ToLowerInvariant();
}

/// <summary>Builds the contact read-model, then welcomes the user.</summary>
public sealed class UserRegisteredConsumer(NotificationsDb db, NotificationService n) : IConsumer<UserRegistered>
{
    public async Task Consume(ConsumeContext<UserRegistered> context)
    {
        var m = context.Message;
        var contact = await db.Contacts.SingleOrDefaultAsync(x => x.UserId == m.UserId);
        if (contact is null)
        {
            db.Contacts.Add(new Contact { UserId = m.UserId, Email = m.Email, Phone = m.Phone, Language = m.Language, UpdatedAt = m.OccurredAt });
        }
        else
        {
            contact.Email = m.Email;
            contact.Phone = m.Phone;
            contact.Language = m.Language;
            contact.UpdatedAt = m.OccurredAt;
        }
        await db.SaveChangesAsync();
        await n.EnqueueAsync(m.UserId, "system", "user.welcome", new() { ["name"] = m.FullName }, [Channel.Email, Channel.InApp], N.Id(context));
    }
}

public sealed class OrderConsumers(NotificationService n) : IConsumer<OrderPaid>, IConsumer<OrderStatusChanged>, IConsumer<OrderCancelled>
{
    private static readonly Dictionary<string, string> Text = new()
    {
        ["Processing"] = "The seller is preparing your order.",
        ["ReadyForPickup"] = "Your parcel is ready for courier pickup.",
        ["PickedUp"] = "Your parcel was picked up.",
        ["AtHub"] = "Your parcel arrived at our hub.",
        ["OutForDelivery"] = "Your parcel is out for delivery today.",
        ["Delivered"] = "Your parcel was delivered. Enjoy!"
    };

    public Task Consume(ConsumeContext<OrderPaid> context) => n.EnqueueAsync(context.Message.CustomerId, "orders", "order.paid",
        new() { ["orderNumber"] = context.Message.OrderNumber, ["total"] = N.Lkr(context.Message.TotalCents) },
        [Channel.Email, Channel.InApp, Channel.Push], N.Id(context), $"/orders/{context.Message.OrderId}");

    public Task Consume(ConsumeContext<OrderStatusChanged> context) =>
        Text.TryGetValue(context.Message.To, out var t)
            ? n.EnqueueAsync(context.Message.CustomerId, "orders", "order.status", new() { ["text"] = t },
                [Channel.InApp, Channel.Push], N.Id(context), $"/orders/{context.Message.OrderId}")
            : Task.CompletedTask;                                          // "Paid" is already covered by OrderPaid; failures by their own events

    public Task Consume(ConsumeContext<OrderCancelled> context) =>
        context.Message.Reason.StartsWith("PAYMENT_FAILED", StringComparison.Ordinal)
            ? Task.CompletedTask                                           // PaymentFailedConsumer already tells the customer (with the cart-restored hint)
            : n.EnqueueAsync(context.Message.CustomerId, "orders", "order.cancelled", new() { ["reason"] = N.Humanize(context.Message.Reason) },
                [Channel.InApp], N.Id(context), $"/orders/{context.Message.OrderId}");
}

public sealed class PaymentFailedConsumer(NotificationService n) : IConsumer<PaymentFailed>
{
    public Task Consume(ConsumeContext<PaymentFailed> context) => n.EnqueueAsync(context.Message.CustomerId, "orders", "payment.failed",
        new() { ["reason"] = N.Humanize(context.Message.FailureCode) }, [Channel.InApp], N.Id(context), $"/orders/{context.Message.OrderId}");
}

/// <summary><c>SellerKycSubmitted</c> has no consumer on purpose: it should notify STAFF (role-based recipients + admin panel, Phase 3).</summary>
public sealed class SellerConsumers(NotificationService n) : IConsumer<SellerApproved>, IConsumer<SellerRejected>, IConsumer<InventoryLowStock>
{
    public Task Consume(ConsumeContext<SellerApproved> context) => n.EnqueueAsync(context.Message.SellerId, "system", "seller.approved",
        new() { ["name"] = context.Message.BusinessName }, [Channel.Email, Channel.InApp], N.Id(context));

    public Task Consume(ConsumeContext<SellerRejected> context) => n.EnqueueAsync(context.Message.SellerId, "system", "seller.rejected",
        new() { ["name"] = "there", ["reason"] = N.Humanize(context.Message.ReasonCode) }, [Channel.Email, Channel.InApp], N.Id(context));

    public Task Consume(ConsumeContext<InventoryLowStock> context) => n.EnqueueAsync(context.Message.SellerId, "system", "stock.low",
        new() { ["sku"] = context.Message.VariantId.ToString()[..8], ["available"] = context.Message.Available.ToString(CultureInfo.InvariantCulture) },
        [Channel.InApp], N.Id(context));
}
