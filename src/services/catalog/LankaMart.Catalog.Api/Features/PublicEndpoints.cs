using System.Text.Json;
using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Catalog.Api.Features;

public static class PublicEndpoints
{
    public static void MapPublic(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Catalog (public)").AllowAnonymous();

        g.MapGet("/categories", async (CatalogDb db, string? lang, CancellationToken ct) =>
        {
            var l = Lang.Normalize(lang);
            var cats = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).Select(c => new
            {
                c.Id,
                c.ParentId,
                c.Slug,
                Name = c.Translations.Where(t => t.Lang == l || t.Lang == "en").OrderBy(t => t.Lang == l ? 0 : 1).Select(t => t.Name).FirstOrDefault() ?? c.Slug
            }).ToListAsync(ct);
            return Results.Ok(cats);
        });

        g.MapGet("/products", async (CatalogDb db, string? query, Guid? category, long? min, long? max, string? sort, string? lang,
                                     int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct) =>
        {
            var l = Lang.Normalize(lang);
            var (p, size) = Paging.Normalize(page, pageSize);

            // Only ACTIVE products of ACTIVE sellers that have at least one active variant are visible.
            var q = db.Products.AsNoTracking()
                .Where(x => x.Status == ProductStatus.Active && x.Variants.Any(v => v.IsActive)
                         && db.KnownSellers.Any(s => s.SellerId == x.SellerId && s.Status == "Active"));
            if (category is not null)
            {
                q = q.Where(x => x.CategoryId == category);
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                var trimmed = query.Trim();
                var pattern = $"%{Like.Escape(trimmed[..Math.Min(trimmed.Length, 100)])}%";   // cap length + escape wildcards
                q = q.Where(x => x.Translations.Any(t => EF.Functions.ILike(t.Name, pattern, "\\")));
            }
            if (min is not null)
            {
                q = q.Where(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents) >= min);
            }
            if (max is not null)
            {
                q = q.Where(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents) <= max);
            }

            // sort: explicit ALLOWLIST — never build ORDER BY from raw user input
            q = sort switch
            {
                "price_asc" => q.OrderBy(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents)),
                "price_desc" => q.OrderByDescending(x => x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents)),
                "rating" => q.OrderByDescending(x => x.RatingAvg).ThenByDescending(x => x.RatingCount),
                _ => q.OrderByDescending(x => x.CreatedAt)
            };

            var total = await q.LongCountAsync(ct);
            var items = await q.Skip((p - 1) * size).Take(size).Select(x => new ProductListItem(
                x.Id,
                x.Translations.Where(t => t.Lang == l || t.Lang == "en").OrderBy(t => t.Lang == l ? 0 : 1).Select(t => t.Slug).FirstOrDefault() ?? "",
                x.Translations.Where(t => t.Lang == l || t.Lang == "en").OrderBy(t => t.Lang == l ? 0 : 1).Select(t => t.Name).FirstOrDefault() ?? "",
                x.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                x.Variants.Where(v => v.IsActive).Min(v => (long?)v.PriceCents) ?? 0,
                x.RatingAvg, x.RatingCount, x.SellerId)).ToListAsync(ct);

            return Results.Ok(new PagedResult<ProductListItem>(items, p, size, total));
        });

        g.MapGet("/products/{slug}", async (string slug, string? lang, CatalogDb db, CancellationToken ct) =>
        {
            var l = Lang.Normalize(lang);
            var productId = await db.ProductTranslations.AsNoTracking().Where(t => t.Slug == slug).Select(t => (Guid?)t.ProductId).FirstOrDefaultAsync(ct)
                            ?? throw new NotFoundException("Product not found");
            var x = await db.Products.AsNoTracking().AsSplitQuery()
                .Include(p => p.Translations).Include(p => p.Variants.Where(v => v.IsActive)).Include(p => p.Images)
                .SingleOrDefaultAsync(p => p.Id == productId && p.Status == ProductStatus.Active, ct) ?? throw new NotFoundException("Product not found");
            if (!await db.KnownSellers.AnyAsync(s => s.SellerId == x.SellerId && s.Status == "Active", ct))
            {
                throw new NotFoundException("Product not found");
            }

            var tr = x.Translations.FirstOrDefault(t => t.Lang == l) ?? x.Translations.First(t => t.Lang == "en");   // fallback chain: requested → en
            return Results.Ok(new
            {
                x.Id,
                slug = tr.Slug,
                name = tr.Name,
                description = tr.Description,
                lang = tr.Lang,
                availableLanguages = x.Translations.Select(t => t.Lang),
                x.Brand,
                x.CategoryId,
                x.SellerId,
                x.RatingAvg,
                x.RatingCount,
                variants = x.Variants.OrderBy(v => v.PriceCents).Select(v => new
                {
                    v.Id,
                    v.Sku,
                    v.PriceCents,
                    v.CompareAtPriceCents,
                    attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(v.AttributesJson) ?? []
                }),
                images = x.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Url, i.Alt, i.VariantId })
            });
        });
    }
}
