using System.Text;
using FluentValidation;
using LankaMart.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Catalog.Api.Features;

public sealed record TranslationDto(string Lang, string Name, string Description);
public sealed record VariantDto(string Sku, Dictionary<string, string>? Attributes, long PriceCents, long? CompareAtPriceCents);
public sealed record ImageDto(string Url, string? Alt);
public sealed record CreateProductRequest(Guid CategoryId, string? Brand, List<TranslationDto> Translations, List<VariantDto> Variants, List<ImageDto>? Images);
public sealed record UpdateProductRequest(Guid CategoryId, string? Brand, List<TranslationDto> Translations);
public sealed record UpdateVariantRequest(long PriceCents, long? CompareAtPriceCents, bool IsActive);

public sealed record ProductListItem(Guid Id, string Slug, string Name, string? ImageUrl, long PriceFromCents, double RatingAvg, int RatingCount, Guid SellerId);

public sealed class TranslationValidator : AbstractValidator<TranslationDto>
{
    public TranslationValidator()
    {
        RuleFor(x => x.Lang).Must(l => Lang.Supported.Contains(l));
        RuleFor(x => x.Name).NotEmpty().Length(3, 200);
        RuleFor(x => x.Description).NotNull().MaximumLength(5000);
    }
}

public sealed class VariantValidator : AbstractValidator<VariantDto>
{
    public VariantValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().Matches(@"^[A-Za-z0-9._-]{2,40}$");
        RuleFor(x => x.PriceCents).GreaterThan(0).LessThan(1_000_000_000);           // < LKR 10,000,000
        RuleFor(x => x.CompareAtPriceCents).GreaterThan(x => x.PriceCents).When(x => x.CompareAtPriceCents is not null);
        RuleFor(x => x.Attributes).Must(a => a is null || (a.Count <= 10 && a.All(kv => kv.Key.Length <= 30 && kv.Value.Length <= 60)));
    }
}

public sealed class ImageValidator : AbstractValidator<ImageDto>
{
    // only http(s) URLs: blocks javascript:/data:/file: URLs that could become stored XSS when rendered in <img src>/<a href>
    public ImageValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(500)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            .WithMessage("Image URL must be an absolute http(s) URL.");
        RuleFor(x => x.Alt).MaximumLength(200);
    }
}

public sealed class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Brand).MaximumLength(80);
        RuleFor(x => x.Translations).NotEmpty().Must(t => t.Any(x => x.Lang == "en")).WithMessage("An English translation is required.")
            .Must(t => t.Select(x => x.Lang).Distinct().Count() == t.Count).WithMessage("Duplicate language.");
        RuleForEach(x => x.Translations).SetValidator(new TranslationValidator());
        RuleFor(x => x.Variants).NotEmpty().Must(v => v.Count <= 50)
            .Must(v => v.Select(x => x.Sku?.ToUpperInvariant()).Distinct().Count() == v.Count).WithMessage("Duplicate SKU.");
        RuleForEach(x => x.Variants).SetValidator(new VariantValidator());
        RuleFor(x => x.Images).Must(i => i is null || i.Count <= 10);
        RuleForEach(x => x.Images!).SetValidator(new ImageValidator()).When(x => x.Images is not null);
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Brand).MaximumLength(80);
        RuleFor(x => x.Translations).NotEmpty().Must(t => t.Any(x => x.Lang == "en"))
            .Must(t => t.Select(x => x.Lang).Distinct().Count() == t.Count).WithMessage("Duplicate language.");
        RuleForEach(x => x.Translations).SetValidator(new TranslationValidator());
    }
}

public sealed class UpdateVariantValidator : AbstractValidator<UpdateVariantRequest>
{
    public UpdateVariantValidator()
    {
        RuleFor(x => x.PriceCents).GreaterThan(0).LessThan(1_000_000_000);
        RuleFor(x => x.CompareAtPriceCents).GreaterThan(x => x.PriceCents).When(x => x.CompareAtPriceCents is not null);
    }
}

public static class Slugs
{
    /// <summary>"Nike Air Zoom 2026!" → "nike-air-zoom-2026". char.IsLetterOrDigit is Unicode-aware, so Sinhala/Tamil slugs work in Phase 3.</summary>
    public static string From(string name)
    {
        var sb = new StringBuilder();
        var dash = false;
        foreach (var ch in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC))    // NFC: one canonical form for Sinhala/Tamil input
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "product" : s[..Math.Min(s.Length, 80)].Trim('-');
    }

    public static async Task<string> UniqueAsync(CatalogDb db, string lang, string name, Guid? exceptProductId, CancellationToken ct)
    {
        var baseSlug = From(name);
        var slug = baseSlug;
        while (await db.ProductTranslations.AnyAsync(t => t.Lang == lang && t.Slug == slug && t.ProductId != exceptProductId, ct))
        {
            slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..4]}";
        }
        return slug;
    }
}
