using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Catalog.Api.Data;

/// <summary>Reference data (categories), created idempotently on startup.</summary>
public static class CatalogSeeder
{
    public static async Task SeedAsync(CatalogDb db)
    {
        string[][] cats = [["electronics","Electronics"],["fashion","Fashion"],["home-living","Home & Living"],["beauty-health","Beauty & Health"],
                           ["groceries","Groceries"],["sports-outdoors","Sports & Outdoors"],["books-stationery","Books & Stationery"],["toys-baby","Toys & Baby"]];
        var order = 0;
        foreach (var c in cats)
        {
            if (await db.Categories.AnyAsync(x => x.Slug == c[0]))
            {
                order++;
                continue;
            }
            var cat = new Category { Id = Guid.CreateVersion7(), Slug = c[0], SortOrder = order++ };
            cat.Translations.Add(new CategoryTranslation { CategoryId = cat.Id, Lang = "en", Name = c[1] });   // si/ta names are added in Phase 3 via the Localization service
            db.Categories.Add(cat);
        }
        await db.SaveChangesAsync();
    }
}
