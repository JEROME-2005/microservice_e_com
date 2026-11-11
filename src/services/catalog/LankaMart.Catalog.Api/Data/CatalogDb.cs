using LankaMart.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Catalog.Api.Data;

public class CatalogDb(DbContextOptions<CatalogDb> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryTranslation> CategoryTranslations => Set<CategoryTranslation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
    public DbSet<ProductVariant> Variants => Set<ProductVariant>();
    public DbSet<ProductImage> Images => Set<ProductImage>();
    public DbSet<KnownSeller> KnownSellers => Set<KnownSeller>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        b.HasDefaultSchema("catalog");
        b.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.CategoryId);
        });
        b.Entity<CategoryTranslation>(e =>
        {
            e.ToTable("category_translations");
            e.HasKey(x => new { x.CategoryId, x.Lang });
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.Status, x.CategoryId });
            e.HasIndex(x => x.SellerId);
            e.HasMany(x => x.Translations).WithOne().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Variants).WithOne(v => v.Product).HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ProductTranslation>(e =>
        {
            e.ToTable("product_translations");
            e.HasKey(x => new { x.ProductId, x.Lang });
            e.HasIndex(x => new { x.Lang, x.Slug }).IsUnique();
        });
        b.Entity<ProductVariant>(e =>
        {
            e.ToTable("product_variants");
            e.HasIndex(x => new { x.SellerId, x.Sku }).IsUnique();
            e.Property(x => x.AttributesJson).HasColumnType("jsonb");
        });
        b.Entity<ProductImage>().ToTable("product_images");
        b.Entity<KnownSeller>(e =>
        {
            e.ToTable("known_sellers");
            e.HasKey(x => x.SellerId);
        });
    }
}
