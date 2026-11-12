using System.Security.Claims;
using FluentValidation;
using LankaMart.Orders.Api.Data;
using LankaMart.Orders.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Orders.Api.Features;

public sealed record CartItemRequest(Guid VariantId, int Quantity);
public sealed record QuantityRequest(int Quantity);
public sealed record MergeRequest(Guid GuestId);

public sealed class CartItemValidator : AbstractValidator<CartItemRequest>
{
    public CartItemValidator() => RuleFor(x => x.VariantId).NotEmpty();
}

public sealed class MergeValidator : AbstractValidator<MergeRequest>
{
    public MergeValidator() => RuleFor(x => x.GuestId).NotEmpty();
}

public static class Endpoints
{
    private static bool IsStaff(ClaimsPrincipal u) => u.IsInRole(Roles.SuperAdmin) || u.IsInRole(Roles.CustomerManager) || u.IsInRole(Roles.AuditManager);

    public static void MapOrders(this WebApplication app)
    {
        // ───── CART: anonymous allowed (guests). Identity = token OR X-Guest-Id ─────
        var cart = app.MapGroup("/api/v1/cart").WithTags("Cart").AllowAnonymous();
        cart.MapGet("", async (HttpContext c, CartService s, CancellationToken ct) => Results.Ok(await s.ViewAsync(CartService.KeyFor(c), ct)));
        cart.MapPost("/items", async (CartItemRequest r, HttpContext c, CartService s) =>
        {
            await s.SetAsync(CartService.KeyFor(c), r.VariantId, r.Quantity, add: true);
            return Results.NoContent();
        }).WithValidation<CartItemRequest>();
        cart.MapPatch("/items/{variantId:guid}", async (Guid variantId, QuantityRequest r, HttpContext c, CartService s) =>
        {
            await s.SetAsync(CartService.KeyFor(c), variantId, r.Quantity, add: false);
            return Results.NoContent();
        }).WithValidation<QuantityRequest>();
        cart.MapDelete("/items/{variantId:guid}", async (Guid variantId, HttpContext c, CartService s) =>
        {
            await s.SetAsync(CartService.KeyFor(c), variantId, 0, add: false);
            return Results.NoContent();
        });
        cart.MapPost("/merge", async (MergeRequest r, HttpContext c, CartService s) =>
        {
            await s.MergeAsync(r.GuestId, c.User.UserId());     // UserId() throws 401 when the caller is anonymous — merging needs a real user
            return Results.NoContent();
        }).WithValidation<MergeRequest>();

        // ───── ORDERS ─────
        var g = app.MapGroup("/api/v1/orders").WithTags("Orders").RequireAuthorization();

        g.MapPost("", async (PlaceOrderRequest r, HttpContext c, CheckoutService svc, CancellationToken ct) =>
        {
            if (!c.User.IsInRole(Roles.Customer))
            {
                throw new ForbiddenException("CUSTOMER_ONLY", "Only customer accounts can place orders.");
            }
            if (!c.User.IsFullyVerified())
            {
                throw new ForbiddenException("VERIFICATION_REQUIRED", "Verify your email and phone before ordering.");
            }
            if (!Guid.TryParse(c.Request.Headers["Idempotency-Key"].FirstOrDefault(), out var key))
            {
                throw new BusinessRuleException("IDEMPOTENCY_KEY_REQUIRED", "Send an Idempotency-Key header containing a GUID.");
            }
            var result = await svc.PlaceOrderAsync(c.User.UserId(), r, key.ToString("N"), ct);
            return Results.Created($"/api/v1/orders/{result.OrderId}", result);
        }).WithValidation<PlaceOrderRequest>();

        g.MapGet("", async (HttpContext c, OrdersDb db, int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var (p, size) = Paging.Normalize(page, pageSize);
            var uid = c.User.UserId();
            var q = db.Orders.AsNoTracking().Where(o => o.CustomerId == uid);                       // ALWAYS filtered by the token's user (admin listing is a Phase 5 feature)
            var total = await q.LongCountAsync(ct);
            var items = await q.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * size).Take(size)
                .Select(o => new { o.Id, o.OrderNumber, status = o.Status.ToString(), o.TotalCents, o.Currency, o.CreatedAt, itemCount = o.Items.Sum(i => i.Quantity) })
                .ToListAsync(ct);
            return Results.Ok(new PagedResult<object>(items.Cast<object>().ToList(), p, size, total));
        });

        g.MapGet("/{id:guid}", async (Guid id, HttpContext c, OrdersDb db, CancellationToken ct) =>
        {
            var o = await LoadAuthorizedAsync(db, id, c, ct);
            return Results.Ok(new
            {
                o.Id, o.OrderNumber, status = o.Status.ToString(), o.SubtotalCents, o.DeliveryFeeCents, o.DiscountCents, o.TotalCents, o.Currency,
                o.PaymentId, o.PaymentExpiresAt, o.CreatedAt, o.CancelReason,
                deliveryAddress = o.DeliveryAddress,
                items = o.Items.Select(i => new { i.VariantId, i.ProductId, name = i.ProductNameSnapshot, i.Sku, imageUrl = i.ImageUrlSnapshot, i.Quantity, i.UnitPriceCents, lineTotalCents = i.LineTotalCents }),
                shipments = o.Shipments.Select(s => new { s.Id, s.SellerId, status = s.Status.ToString() })
            });
        });

        g.MapGet("/{id:guid}/tracking", async (Guid id, HttpContext c, OrdersDb db, CancellationToken ct) =>
        {
            var o = await LoadAuthorizedAsync(db, id, c, ct);
            return Results.Ok(new
            {
                o.OrderNumber,
                status = o.Status.ToString(),
                timeline = o.Events.OrderBy(e => e.At).Select(e => new { status = e.ToStatus.ToString(), e.Note, at = e.At })
            });
        });

        g.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext c, OrdersDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var o = await LoadAuthorizedAsync(db, id, c, ct);
            if (o.CustomerId != c.User.UserId())
            {
                throw new NotFoundException();               // staff/sellers may VIEW, only the owner may cancel
            }
            var now = clock.GetUtcNow();
            // Phase 1: customers can cancel only BEFORE paying. Cancelling a paid order needs the refund flow (Phase 5).
            // Transition + OrderCancelled (→ Inventory releases the stock) commit in ONE transaction.
            var cancelled = await db.InTransactionAsync(async () =>
            {
                if (!await OrderTransitions.TryAsync(db, o.Id, OrderStatus.PendingPayment, OrderStatus.Cancelled, "customer", "Cancelled by customer", now, "CUSTOMER_CANCELLED"))
                {
                    return false;
                }
                await bus.Publish(new OrderCancelled(o.Id, o.CustomerId, "CUSTOMER_CANCELLED", CheckoutService.ToLines(o), now), CancellationToken.None);
                return true;
            }, CancellationToken.None);
            if (!cancelled)
            {
                throw new BusinessRuleException("CANNOT_CANCEL", "Only orders awaiting payment can be cancelled right now.");
            }
            return Results.NoContent();
        });

        // ───── DEV SIMULATOR: stands in for the Logistics service until Phase 4 ─────
        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/api/v1/dev/orders/{id:guid}/advance", async (Guid id, OrdersDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
            {
                var o = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
                var next = OrderStateMachine.Next(o.Status) ?? throw new BusinessRuleException("NO_NEXT_STATE", $"No next step from {o.Status}.");
                var now = clock.GetUtcNow();
                var moved = await db.InTransactionAsync(async () =>
                {
                    if (!await OrderTransitions.TryAsync(db, id, o.Status, next, "dev-simulator", "Simulated logistics step", now))
                    {
                        return false;
                    }
                    await bus.Publish(new OrderStatusChanged(id, o.CustomerId, o.Status.ToString(), next.ToString(), now), ct);
                    return true;
                }, ct);
                if (!moved)
                {
                    throw new ConflictException("CONCURRENT_UPDATE", "The order changed meanwhile; try again.");
                }
                return Results.Ok(new { status = next.ToString() });
            }).WithTags("Dev").RequireAuthorization(Policies.SuperAdmin);
        }

        static async Task<Order> LoadAuthorizedAsync(OrdersDb db, Guid id, HttpContext c, CancellationToken ct)
        {
            var o = await db.Orders.AsNoTracking().Include(x => x.Items).Include(x => x.Shipments).Include(x => x.Events).AsSplitQuery()
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            // CanView policy (architecture doc §4.1): owner OR a seller who has items in it OR an authorised staff role.
            // Anything else → 404, not 403 (don't confirm the order exists).
            var uid = c.User.UserId();
            var allowed = o is not null && (o.CustomerId == uid || o.Shipments.Any(s => s.SellerId == uid) || IsStaff(c.User));
            return allowed ? o! : throw new NotFoundException();
        }
    }
}
