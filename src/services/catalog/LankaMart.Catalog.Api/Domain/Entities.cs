namespace LankaMart.Catalog.Api.Domain;

public enum ProductStatus { Draft, PendingModeration, Active, Rejected, Archived }   // PendingModeration/Rejected are used from Phase 3

public class Category
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Slug { get; set; } = "";
    public int SortOrder { get; set; }
    public List<CategoryTranslation> Translations { get; set; } = [];
}

public class CategoryTranslation
{
    public Guid CategoryId { get; set; }
    public string Lang { get; set; } = "en";
    public string Name { get; set; } = "";
}

public class Product
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public Guid CategoryId { get; set; }
    public string? Brand { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public double RatingAvg { get; set; }                   // denormalised; filled by reviews in Phase 3
    public int RatingCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ProductTranslation> Translations { get; set; } = [];
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductImage> Images { get; set; } = [];
}

// One row per language → adding Tamil later needs NO migration, and a missing translation falls back to English.
public class ProductTranslation
{
    public Guid ProductId { get; set; }
    public string Lang { get; set; } = "en";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class ProductVariant
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid SellerId { get; set; }                        // denormalised so (seller, sku) can be unique
    public string Sku { get; set; } = "";
    public string AttributesJson { get; set; } = "{}";        // e.g. {"color":"Black","size":"42"} stored as jsonb
    public long PriceCents { get; set; }
    public long? CompareAtPriceCents { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProductImage
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public string Url { get; set; } = "";
    public string? Alt { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Catalog's local copy of "which sellers may sell", built from SellerApproved/Suspended events — so it never has to call the Users service.</summary>
public class KnownSeller
{
    public Guid SellerId { get; set; }
    public string Status { get; set; } = "Registered";
    public DateTimeOffset UpdatedAt { get; set; }
}
