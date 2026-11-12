using LankaMart.BuildingBlocks;
using LankaMart.Contracts.V1;
using LankaMart.Orders.Api.Consumers;
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using LankaMart.Orders.Api.Features;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.Orders.Tests;

/// <summary>Phase 2 Sprint 2 (guide §2.4): the outbox makes events as durable as the data, the inbox makes a duplicate delivery harmless.
/// Real Postgres (Testcontainers) + the exact outbox/inbox wiring the services use (<see cref="MessagingExtensions.AddPlatformOutbox{TDbContext}"/>),
/// on MassTransit's in-memory transport.</summary>
public sealed class OutboxInboxTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<OrdersDb>(o => o
            .UseNpgsql(pg.ConnectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "orders"))
            .UseSnakeCaseNamingConvention());
        services.AddMassTransitTestHarness(x =>
        {
            x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(30), testInactivityTimeout: TimeSpan.FromSeconds(3));
            x.AddConsumer<PaymentCompletedConsumer>();
            x.AddConsumer<NonIdempotentPingConsumer>();
            x.AddConsumer<EventSink>();                // a downstream "service": lets the test see what really left the outbox, typed
            x.AddPlatformOutbox<OrdersDb>();
        });
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task Duplicate_delivery_of_the_same_message_id_has_exactly_one_effect()
    {
        await using var sp = BuildServices();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var order = await SeedOrderAsync(sp);
            var messageId = NewId.NextGuid();

            // A consumer that is NOT idempotent on its own: every call appends a row. Only the inbox can keep this at one.
            var ping = new PingOrder(order.Id);
            await harness.Bus.Publish(ping, c => c.MessageId = messageId);
            await harness.Bus.Publish(ping, c => c.MessageId = messageId);
            await WaitForReceiveCountAsync(messageId, 2);

            Assert.Equal(1, await CountAsync($"SELECT count(*) FROM orders.order_events WHERE order_id = '{order.Id}' AND note = 'ping'"));
            Assert.Equal(1, await CountAsync($"SELECT count(*) FROM orders.inbox_state WHERE message_id = '{messageId}'"));
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task PaymentCompleted_delivered_twice_produces_one_OrderPaid()
    {
        await using var sp = BuildServices();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var order = await SeedOrderAsync(sp);
            var messageId = NewId.NextGuid();
            var msg = new PaymentCompleted(Guid.NewGuid(), order.Id, order.CustomerId, order.TotalCents, "LKR", "mock_ref", DateTimeOffset.UtcNow);

            await harness.Bus.Publish(msg, c => c.MessageId = messageId);
            await harness.Bus.Publish(msg, c => c.MessageId = messageId);
            await WaitForReceiveCountAsync(messageId, 2);

            Assert.True(await LeftTheOutboxAsync<OrderPaid>(harness, m => m.OrderId == order.Id));
            await harness.InactivityTask;                                   // nothing else in flight → a second OrderPaid would have arrived by now
            Assert.Single(harness.Consumed.Select<OrderPaid>(m => m.Context.Message.OrderId == order.Id));
            Assert.Equal(1, await CountAsync($"SELECT count(*) FROM orders.inbox_state WHERE message_id = '{messageId}'"));
            Assert.Equal(OrderStatus.Paid, await StatusAsync(sp, order.Id));
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Exception_after_publish_rolls_back_both_the_state_change_and_the_outbox_row()
    {
        await using var sp = BuildServices();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var order = await SeedOrderAsync(sp);
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrdersDb>();
                var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();     // the scoped (bus-outbox) endpoint, as in an HTTP request
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.InTransactionAsync(async () =>
                {
                    Assert.True(await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, OrderStatus.Cancelled, "test", null, DateTimeOffset.UtcNow, "TEST"));
                    await bus.Publish(new OrderCancelled(order.Id, order.CustomerId, "TEST", [], DateTimeOffset.UtcNow));
                    throw new InvalidOperationException("crash after publish");
                }));
            }

            Assert.Equal(OrderStatus.PendingPayment, await StatusAsync(sp, order.Id));
            Assert.Equal(0, await CountAsync($"SELECT count(*) FROM orders.outbox_message WHERE body LIKE '%{order.Id}%'"));
            Assert.False(await LeftTheOutboxAsync<OrderCancelled>(harness, m => m.OrderId == order.Id));
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Committed_transition_is_delivered_by_the_outbox()
    {
        await using var sp = BuildServices();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var order = await SeedOrderAsync(sp);
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrdersDb>();
                var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                await db.InTransactionAsync(async () =>
                {
                    Assert.True(await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, OrderStatus.Cancelled, "test", null, DateTimeOffset.UtcNow, "TEST"));
                    await bus.Publish(new OrderCancelled(order.Id, order.CustomerId, "TEST", [], DateTimeOffset.UtcNow));
                });
            }

            Assert.Equal(OrderStatus.Cancelled, await StatusAsync(sp, order.Id));
            Assert.True(await LeftTheOutboxAsync<OrderCancelled>(harness, m => m.OrderId == order.Id));   // the delivery service sent it
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Publish_without_SaveChanges_is_silently_dropped()
    {
        // THE RULE (guide §2.3): a Publish is only durable if a SaveChanges follows it on the same DbContext.
        await using var sp = BuildServices();
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.NewGuid();
            using (var scope = sp.CreateScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                await bus.Publish(new OrderStatusChanged(orderId, Guid.NewGuid(), "Paid", "Processing", DateTimeOffset.UtcNow));
            }
            Assert.False(await LeftTheOutboxAsync<OrderStatusChanged>(harness, m => m.OrderId == orderId));
        }
        finally
        {
            await harness.Stop();
        }
    }

    /// <summary>The outbox delivers an event by SENDING its stored, already-serialized body to the publish address, so the harness's
    /// Sent/Published lists only see an untyped body. What a downstream consumer RECEIVES is typed → assert on that.</summary>
    private static Task<bool> LeftTheOutboxAsync<T>(ITestHarness harness, Func<T, bool> match) where T : class =>
        harness.Consumed.Any<T>(m => match(m.Context.Message));

    private async Task WaitForReceiveCountAsync(Guid messageId, int expected)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            var n = await CountAsync($"SELECT coalesce(max(receive_count), 0) FROM orders.inbox_state WHERE message_id = '{messageId}' AND consumed IS NOT NULL");
            if (n >= expected)
            {
                await Task.Delay(500);           // let the second delivery's (no-op) transaction finish
                return;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException($"Message {messageId} was not received {expected}× within 30 s");
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var conn = new Npgsql.NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<OrderStatus> StatusAsync(IServiceProvider sp, Guid orderId)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDb>();
        return await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync();
    }

    private static async Task<Order> SeedOrderAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDb>();
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id = Guid.CreateVersion7(), OrderNumber = $"LM-TEST-{Guid.NewGuid():N}"[..20], CustomerId = Guid.NewGuid(), Status = OrderStatus.PendingPayment,
            SubtotalCents = 1000, TotalCents = 1000, CreatedAt = now, UpdatedAt = now, PaymentExpiresAt = now.AddMinutes(15),
            DeliveryAddress = new AddressSnapshot { RecipientName = "Test", Phone = "+94771234567", Line1 = "1 Test Road", City = "Colombo", District = "Colombo" }
        };
        order.Items.Add(new OrderItem { Id = Guid.CreateVersion7(), OrderId = order.Id, VariantId = Guid.NewGuid(), ProductId = Guid.NewGuid(), SellerId = Guid.NewGuid(), ProductNameSnapshot = "Thing", Sku = "T-1", Quantity = 1, UnitPriceCents = 1000 });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }
}

public sealed record PingOrder(Guid OrderId);

/// <summary>Stands in for the downstream services (Inventory, Notifications…): receives the events Orders emits.</summary>
public sealed class EventSink : IConsumer<OrderPaid>, IConsumer<OrderCancelled>, IConsumer<OrderStatusChanged>
{
    public Task Consume(ConsumeContext<OrderPaid> context) => Task.CompletedTask;
    public Task Consume(ConsumeContext<OrderCancelled> context) => Task.CompletedTask;
    public Task Consume(ConsumeContext<OrderStatusChanged> context) => Task.CompletedTask;
}

/// <summary>Deliberately NOT idempotent: appends an order_events row on every call.</summary>
public sealed class NonIdempotentPingConsumer(OrdersDb db) : IConsumer<PingOrder>
{
    public async Task Consume(ConsumeContext<PingOrder> context)
    {
        db.Events.Add(new OrderEvent { Id = Guid.NewGuid(), OrderId = context.Message.OrderId, ToStatus = OrderStatus.PendingPayment, Actor = "test", Note = "ping", At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}
