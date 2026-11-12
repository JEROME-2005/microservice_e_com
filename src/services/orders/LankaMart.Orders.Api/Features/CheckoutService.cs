using FluentValidation;
using LankaMart.Orders.Api.Clients;
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Order = LankaMart.Orders.Api.Domain.Order;   // StackExchange.Redis also defines an "Order" enum

namespace LankaMart.Orders.Api.Features;

public sealed record PlaceOrderRequest(AddressInput DeliveryAddress);
public sealed record AddressInput(string RecipientName, string Phone, string Line1, string? Line2, string City, string District, string? PostalCode);
public sealed record CheckoutResult(Guid OrderId, string OrderNumber, long TotalCents, string Currency, Guid PaymentId, DateTimeOffset PaymentExpiresAt);

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.DeliveryAddress).NotNull().ChildRules(a =>
        {
            a.RuleFor(x => x.RecipientName).NotEmpty().Length(2, 100);
            a.RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalizeLk(p, out _)).WithMessage("Enter a valid Sri Lankan mobile number.");
            a.RuleFor(x => x.Line1).NotEmpty().MaximumLength(150);
            a.RuleFor(x => x.Line2).MaximumLength(150);
            a.RuleFor(x => x.City).NotEmpty().MaximumLength(80);
            a.RuleFor(x => x.District).Must(d => SriLanka.Districts.Contains(d)).WithMessage("Unknown district.");
            a.RuleFor(x => x.PostalCode).Matches(@"^\d{5}$").When(x => !string.IsNullOrEmpty(x.PostalCode));
        });
    }
}

/// <summary>The checkout saga (orchestrator). The order row IS the saga state: each step persists before the next external call,
/// and every failure path ends in a consistent state (see docs/adr/0008-explicit-checkout-saga.md).</summary>
public sealed class CheckoutService(OrdersDb db, CartService carts, ICatalogClient catalog, IInventoryClient inventory, IPaymentsClient payments,
    IPublishEndpoint bus, IConnectionMultiplexer redis, IOptions<OrdersOptions> opt, TimeProvider clock, ILogger<CheckoutService> log)
{
    // ───────────── Step 0: idempotency wrapper ─────────────
    public async Task<CheckoutResult> PlaceOrderAsync(Guid userId, PlaceOrderRequest req, string idemKey, CancellationToken ct)
    {
        var key = $"idem:orders:{userId}:{idemKey}";
        var r = redis.GetDatabase();
        // SET NX: only the FIRST request with this key wins the right to create the order.
        if (!await r.StringSetAsync(key, "pending", TimeSpan.FromHours(24), When.NotExists))
        {
            string? v = await r.StringGetAsync(key);
            if (v is null or "pending")
            {
                throw new ConflictException("REQUEST_IN_PROGRESS", "This checkout is already being processed.");
            }
            return await LoadResultAsync(Guid.Parse(v), userId, ct);                  // retry of a finished request → same answer, no duplicate order
        }
        try
        {
            var result = await PlaceCoreAsync(userId, req, ct);
            await r.StringSetAsync(key, result.OrderId.ToString(), TimeSpan.FromHours(24));
            return result;
        }
        catch
        {
            await r.KeyDeleteAsync(key);                                                // failed attempt → allow a fresh retry with the same key
            throw;
        }
    }

    private async Task<CheckoutResult> PlaceCoreAsync(Guid userId, PlaceOrderRequest req, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // ───────────── Step 1: read the cart ─────────────
        var cartKey = CartService.UserKey(userId);
        var cart = await carts.RawAsync(cartKey);
        if (cart.Count == 0)
        {
            throw new BusinessRuleException("CART_EMPTY", "Your cart is empty.");
        }

        // ───────────── Step 2: AUTHORITATIVE prices from Catalog (the client's numbers are never trusted) ─────────────
        IReadOnlyList<VariantInfo> lookup;
        try
        {
            lookup = await catalog.LookupAsync(cart.Keys, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        {
            log.LogError(ex, "Catalog lookup failed during checkout");
            throw new ServiceUnavailableException("CATALOG_UNAVAILABLE", "We couldn't check prices. Please try again.");   // nothing created yet → nothing to compensate
        }
        var info = lookup.ToDictionary(v => v.VariantId);
        var bad = cart.Keys.Where(id => !info.TryGetValue(id, out var v) || !v.IsPurchasable).ToList();
        if (bad.Count > 0)
        {
            throw new ConflictException("ITEM_UNAVAILABLE", "Some items are no longer available. Please review your cart.");
        }

        // ───────────── Step 3: create the order (state PENDING_PAYMENT) — saga state is now durable ─────────────
        var orderNumber = $"LM-{now.Year}-{await NextSequenceAsync(ct)}";
        PhoneNumber.TryNormalizeLk(req.DeliveryAddress.Phone, out var phone);
        var a = req.DeliveryAddress;
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            OrderNumber = orderNumber,
            CustomerId = userId,
            Status = OrderStatus.PendingPayment,
            Currency = "LKR",
            CreatedAt = now,
            UpdatedAt = now,
            PaymentExpiresAt = now.AddMinutes(opt.Value.PaymentWindowMinutes),
            DeliveryAddress = new AddressSnapshot
            {
                RecipientName = a.RecipientName.Trim(), Phone = phone, Line1 = a.Line1.Trim(), Line2 = a.Line2?.Trim(),
                City = a.City.Trim(), District = a.District, PostalCode = a.PostalCode
            }
        };
        foreach (var (variantId, qty) in cart)
        {
            var v = info[variantId];
            order.Items.Add(new OrderItem
            {
                Id = Guid.CreateVersion7(), OrderId = order.Id, VariantId = variantId, ProductId = v.ProductId, SellerId = v.SellerId,
                ProductNameSnapshot = v.ProductName, Sku = v.Sku, ImageUrlSnapshot = v.ImageUrl, Quantity = qty, UnitPriceCents = v.PriceCents
            });
        }
        foreach (var sellerId in order.Items.Select(i => i.SellerId).Distinct())
        {
            order.Shipments.Add(new Shipment { Id = Guid.CreateVersion7(), OrderId = order.Id, SellerId = sellerId });
        }
        order.SubtotalCents = order.Items.Sum(i => checked(i.UnitPriceCents * i.Quantity));
        order.DeliveryFeeCents = order.Shipments.Count * opt.Value.DeliveryFeePerShipmentCents;       // one flat fee per seller shipment in Phase 1
        order.TotalCents = order.SubtotalCents + order.DeliveryFeeCents - order.DiscountCents;
        order.Events.Add(new OrderEvent { Id = Guid.CreateVersion7(), OrderId = order.Id, ToStatus = OrderStatus.PendingPayment, Actor = "customer", At = now, Note = "Order placed" });
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // ───────────── Step 4: reserve stock (sync, atomic in Redis) ─────────────
        ReserveStockResponse reserve;
        try
        {
            reserve = await inventory.ReserveAsync(new ReserveStockRequest(order.Id, order.Items.Select(i => new StockLine(i.VariantId, i.Quantity)).ToList(),
                                                    (int)TimeSpan.FromMinutes(opt.Value.ReservationTtlMinutes).TotalSeconds), ct);
        }
        catch (Exception ex)
        {
            await CompensateAsync(order, OrderStatus.Cancelled, "INVENTORY_UNAVAILABLE", ex);
            throw new ServiceUnavailableException("INVENTORY_UNAVAILABLE", "We couldn't check stock. Please try again.");
        }

        if (!reserve.Success)
        {
            await CompensateAsync(order, OrderStatus.Cancelled, "OUT_OF_STOCK", null);
            throw new ConflictException("OUT_OF_STOCK", "Sorry, some items just sold out. Please review your cart.");
        }

        // ───────────── Step 5: create the payment intent ─────────────
        CreatePaymentIntentResponse intent;
        try
        {
            intent = await payments.CreateIntentAsync(new CreatePaymentIntentRequest(order.Id, userId, order.TotalCents, order.Currency), ct);
        }
        catch (Exception ex)
        {
            await CompensateAsync(order, OrderStatus.Cancelled, "PAYMENT_UNAVAILABLE", ex);          // COMPENSATION: undo the reservation (via OrderCancelled → Inventory releases)
            throw new ServiceUnavailableException("PAYMENT_UNAVAILABLE", "Payments are temporarily unavailable. Please try again.");
        }
        // ───────────── Step 6: success path — store the payment id AND announce, atomically; then clear the cart ─────────────
        order.PaymentId = intent.PaymentId;                                                                     // the order is still tracked
        await bus.Publish(new OrderCreated(order.Id, order.OrderNumber, userId, order.TotalCents, now), CancellationToken.None);   // outbox row …
        await db.SaveChangesAsync(CancellationToken.None);                                                      // … committed with the payment id
        await carts.ClearAsync(cartKey);                                                                        // Redis clean-up after the durable commit
        return new CheckoutResult(order.Id, order.OrderNumber, order.TotalCents, order.Currency, intent.PaymentId, order.PaymentExpiresAt);
    }

    /// <summary>Compensating action: move the order to a terminal state and publish OrderCancelled so Inventory releases the stock.
    /// Transition + event commit in ONE transaction (outbox), so "cancelled but stock never released" can't happen any more;
    /// the 17-minute reservation TTL + sweeper stays as the last-resort backstop.
    /// Uses CancellationToken.None on purpose: a client that hangs up mid-checkout must not stop the clean-up.</summary>
    private async Task CompensateAsync(Order order, OrderStatus to, string reason, Exception? ex)
    {
        if (ex is not null)
        {
            log.LogError(ex, "Checkout compensation for {OrderId}: {Reason}", order.Id, reason);
        }
        var now = clock.GetUtcNow();
        await db.InTransactionAsync(async () =>
        {
            if (await OrderTransitions.TryAsync(db, order.Id, OrderStatus.PendingPayment, to, "system", reason, now, reason))
            {
                await bus.Publish(new OrderCancelled(order.Id, order.CustomerId, reason, ToLines(order), now), CancellationToken.None);
            }
        }, CancellationToken.None);
    }

    public static IReadOnlyList<OrderLine> ToLines(Order o) => o.Items.Select(i => new OrderLine(i.VariantId, i.SellerId, i.Quantity, i.UnitPriceCents)).ToList();

    private async Task<string> NextSequenceAsync(CancellationToken ct) =>
        (await db.Database.SqlQuery<long>($"SELECT nextval('orders.order_number_seq') AS \"Value\"").SingleAsync(ct)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<CheckoutResult> LoadResultAsync(Guid orderId, Guid userId, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId && x.CustomerId == userId, ct) ?? throw new NotFoundException();
        return new CheckoutResult(o.Id, o.OrderNumber, o.TotalCents, o.Currency, o.PaymentId ?? Guid.Empty, o.PaymentExpiresAt);
    }
}
