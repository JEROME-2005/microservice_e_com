using LankaMart.Catalog.Api.Data;
using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Catalog.Api.Features;

public static class InternalEndpoints
{
    public static void MapInternal(this WebApplication app)
    {
        var g = app.MapInternalGroup("/api/v1/internal");      // JWT not needed, internal key REQUIRED, and the gateway has no route to it

        // Orders asks "what is the REAL price/name/seller of these variants?" — the client's price is never trusted.
        g.MapPost("/variants/lookup", async (VariantLookupRequest req, CatalogDb db, CancellationToken ct) =>
        {
            var ids = (req.VariantIds ?? []).Distinct().Take(100).ToList();
            var rows = await db.Variants.AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => new
            {
                v.Id,
                v.ProductId,
                v.SellerId,
                v.Sku,
                v.PriceCents,
                v.IsActive,
                ProductStatus = v.Product.Status,
                Name = v.Product.Translations.Where(t => t.Lang == "en").Select(t => t.Name).FirstOrDefault() ?? v.Sku,
                Image = v.Product.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
            }).ToListAsync(ct);

            var sellerIds = rows.Select(r => r.SellerId).Distinct().ToList();
            var active = (await db.KnownSellers.Where(s => sellerIds.Contains(s.SellerId) && s.Status == "Active").Select(s => s.SellerId).ToListAsync(ct)).ToHashSet();

            var result = rows.Select(r => new VariantInfo(r.Id, r.ProductId, r.SellerId, r.Sku, r.Name, r.Image, r.PriceCents,
                IsPurchasable: r.IsActive && r.ProductStatus == ProductStatus.Active && active.Contains(r.SellerId))).ToList();
            return Results.Ok(new VariantLookupResponse(result));
        });
    }
}
