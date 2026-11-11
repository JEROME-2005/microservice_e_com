using System.Text.Json;
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LankaMart.Catalog.Api.Features;

/// <summary>"View-only until approved" enforced at the API (System Scenarios §3.1). A crafty seller calling POST directly with an unapproved token gets 403 SELLER_NOT_APPROVED.</summary>
public sealed class RequireActiveSellerFilter(CatalogDb db) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var sellerId = context.HttpContext.User.SellerId();
        if (!await db.KnownSellers.AnyAsync(s => s.SellerId == sellerId && s.Status == "Active"))
        {
            throw new ForbiddenException("SELLER_NOT_APPROVED", "Your seller account is not approved yet.");
        }
        return await next(context);
    }
}

public static class SellerEndpoints
{
    private static bool IsUniqueViolation(DbUpdateException e) => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static void MapSeller(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/seller/products").WithTags("Seller products")
                   .RequireAuthorization(Policies.Seller).AddEndpointFilter<RequireActiveSellerFilter>();

        g.MapGet("", async (HttpContext c, CatalogDb db, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var list = await db.Products.AsNoTracking().Where(p => p.SellerId == sid).OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    p.Id,
                    status = p.Status.ToString(),
                    name = p.Translations.Where(t => t.Lang == "en").Select(t => t.Name).FirstOrDefault(),
                    variants = p.Variants.Select(v => new { v.Id, v.Sku, v.PriceCents, v.IsActive }),
                    p.CreatedAt
                }).ToListAsync(ct);
            return Results.Ok(list);
        });

        g.MapPost("", async (CreateProductRequest r, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var now = clock.GetUtcNow();
            if (!await db.Categories.AnyAsync(x => x.Id == r.CategoryId, ct))
            {
                throw new BusinessRuleException("UNKNOWN_CATEGORY", "Category does not exist.");
            }

            var p = new Product { Id = Guid.CreateVersion7(), SellerId = sid, CategoryId = r.CategoryId, Brand = r.Brand?.Trim(), Status = ProductStatus.Draft, CreatedAt = now, UpdatedAt = now };
            foreach (var t in r.Translations)
            {
                p.Translations.Add(new ProductTranslation { ProductId = p.Id, Lang = t.Lang, Name = t.Name.Trim(), Description = t.Description.Trim(), Slug = await Slugs.UniqueAsync(db, t.Lang, t.Name, null, ct) });
            }
            foreach (var v in r.Variants)
            {
                p.Variants.Add(new ProductVariant { Id = Guid.CreateVersion7(), SellerId = sid, Sku = v.Sku, AttributesJson = JsonSerializer.Serialize(v.Attributes ?? []), PriceCents = v.PriceCents, CompareAtPriceCents = v.CompareAtPriceCents });
            }
            var order = 0;
            foreach (var i in r.Images ?? [])
            {
                p.Images.Add(new ProductImage { Id = Guid.CreateVersion7(), Url = i.Url, Alt = i.Alt, SortOrder = order++ });
            }

            var skus = r.Variants.Select(x => x.Sku).ToList();
            if (await db.Variants.AnyAsync(v => v.SellerId == sid && skus.Contains(v.Sku), ct))
            {
                throw new ConflictException("SKU_TAKEN", "One of your SKUs is already used.");
            }
            db.Products.Add(p);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (IsUniqueViolation(e))     // the unique index (seller_id, sku) is the real guarantee
            {
                throw new ConflictException("SKU_TAKEN", "One of your SKUs is already used.");
            }
            foreach (var v in p.Variants)
            {
                await bus.Publish(new VariantCreated(v.Id, p.Id, sid, v.Sku, now), ct);    // Inventory creates the stock row
            }
            return Results.Created($"/api/v1/seller/products/{p.Id}", new { p.Id, status = p.Status.ToString(), variantIds = p.Variants.Select(v => v.Id) });
        }).WithValidation<CreateProductRequest>();

        g.MapPut("/{id:guid}", async (Guid id, UpdateProductRequest r, HttpContext c, CatalogDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var p = await db.Products.Include(x => x.Translations).SingleOrDefaultAsync(x => x.Id == id && x.SellerId == sid, ct) ?? throw new NotFoundException();   // ownership in the query
            if (!await db.Categories.AnyAsync(x => x.Id == r.CategoryId, ct))
            {
                throw new BusinessRuleException("UNKNOWN_CATEGORY", "Category does not exist.");
            }
            p.CategoryId = r.CategoryId;
            p.Brand = r.Brand?.Trim();
            p.UpdatedAt = clock.GetUtcNow();
            foreach (var t in r.Translations)
            {
                var row = p.Translations.FirstOrDefault(x => x.Lang == t.Lang);
                if (row is null)
                {
                    p.Translations.Add(new ProductTranslation { ProductId = p.Id, Lang = t.Lang, Name = t.Name.Trim(), Description = t.Description.Trim(), Slug = await Slugs.UniqueAsync(db, t.Lang, t.Name, p.Id, ct) });
                }
                else
                {
                    row.Name = t.Name.Trim();          // slug stays stable so shared links don't break
                    row.Description = t.Description.Trim();
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithValidation<UpdateProductRequest>();

        g.MapPost("/{id:guid}/variants", async (Guid id, VariantDto v, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            if (!await db.Products.AnyAsync(x => x.Id == id && x.SellerId == sid, ct))
            {
                throw new NotFoundException();
            }
            if (await db.Variants.AnyAsync(x => x.SellerId == sid && x.Sku == v.Sku, ct))
            {
                throw new ConflictException("SKU_TAKEN", "SKU already used.");
            }
            var nv = new ProductVariant { Id = Guid.CreateVersion7(), ProductId = id, SellerId = sid, Sku = v.Sku, AttributesJson = JsonSerializer.Serialize(v.Attributes ?? []), PriceCents = v.PriceCents, CompareAtPriceCents = v.CompareAtPriceCents };
            db.Variants.Add(nv);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (IsUniqueViolation(e))
            {
                throw new ConflictException("SKU_TAKEN", "SKU already used.");
            }
            await bus.Publish(new VariantCreated(nv.Id, id, sid, nv.Sku, clock.GetUtcNow()), ct);
            return Results.Created($"/api/v1/seller/products/{id}/variants/{nv.Id}", new { nv.Id });
        }).WithValidation<VariantDto>();

        g.MapPut("/{id:guid}/variants/{variantId:guid}", async (Guid id, Guid variantId, UpdateVariantRequest r, HttpContext c, CatalogDb db, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var v = await db.Variants.SingleOrDefaultAsync(x => x.Id == variantId && x.ProductId == id && x.SellerId == sid, ct) ?? throw new NotFoundException();
            v.PriceCents = r.PriceCents;
            v.CompareAtPriceCents = r.CompareAtPriceCents;
            v.IsActive = r.IsActive;
            await db.SaveChangesAsync(ct);   // price edits stay live (System Scenarios §3.4); title/image edits will need re-moderation in Phase 3
            return Results.NoContent();
        }).WithValidation<UpdateVariantRequest>();

        g.MapPost("/{id:guid}/publish", async (Guid id, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, IConfiguration cfg, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var p = await db.Products.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == id && x.SellerId == sid, ct) ?? throw new NotFoundException();
            if (p.Status is not (ProductStatus.Draft or ProductStatus.Archived or ProductStatus.Rejected))
            {
                throw new BusinessRuleException("INVALID_STATE", $"Cannot publish a {p.Status} product.");
            }
            if (!p.Variants.Any(v => v.IsActive))
            {
                throw new BusinessRuleException("NO_ACTIVE_VARIANT", "Add at least one active variant first.");
            }
            var old = p.Status;
            // Phase 3 flips this switch: Draft → PendingModeration → (Seller Manager approves) → Active.
            p.Status = cfg.GetValue("Catalog:ModerationEnabled", false) ? ProductStatus.PendingModeration : ProductStatus.Active;
            p.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await bus.Publish(new ProductStatusChanged(p.Id, sid, old.ToString(), p.Status.ToString(), p.UpdatedAt), ct);
            return Results.Ok(new { status = p.Status.ToString() });
        });

        g.MapPost("/{id:guid}/archive", async (Guid id, HttpContext c, CatalogDb db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var sid = c.User.SellerId();
            var p = await db.Products.SingleOrDefaultAsync(x => x.Id == id && x.SellerId == sid, ct) ?? throw new NotFoundException();
            var old = p.Status;
            p.Status = ProductStatus.Archived;
            p.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await bus.Publish(new ProductStatusChanged(p.Id, sid, old.ToString(), "Archived", p.UpdatedAt), ct);
            return Results.NoContent();
        });
    }
}
