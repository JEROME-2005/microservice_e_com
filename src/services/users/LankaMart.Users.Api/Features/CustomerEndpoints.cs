using FluentValidation;
using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Users.Api.Features;

public sealed record UpdateProfileRequest(string FullName, string PreferredLanguage);
public sealed record AddressRequest(string Label, string RecipientName, string Phone, string Line1, string? Line2, string City, string District, string? PostalCode, bool IsDefault);
public sealed record AddressDto(Guid Id, string Label, string RecipientName, string Phone, string Line1, string? Line2, string City, string District, string? PostalCode, bool IsDefault);

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.PreferredLanguage).Must(l => Lang.Supported.Contains(l));
    }
}

public sealed class AddressValidator : AbstractValidator<AddressRequest>
{
    public AddressValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(30);
        RuleFor(x => x.RecipientName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Phone).Must(p => PhoneNumber.TryNormalizeLk(p, out _)).WithMessage("Enter a valid Sri Lankan mobile number.");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Line2).MaximumLength(150);
        RuleFor(x => x.City).NotEmpty().MaximumLength(80);
        RuleFor(x => x.District).Must(d => SriLanka.Districts.Contains(d)).WithMessage("Unknown district.");
        RuleFor(x => x.PostalCode).Matches(@"^\d{5}$").When(x => !string.IsNullOrEmpty(x.PostalCode));
    }
}

public static class CustomerEndpoints
{
    private static AddressDto ToDto(Address a) => new(a.Id, a.Label, a.RecipientName, a.Phone, a.Line1, a.Line2, a.City, a.District, a.PostalCode, a.IsDefault);

    public static void MapCustomers(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/customers/me").WithTags("Customers").RequireAuthorization(Policies.Customer);

        // NOTE: no {id} in these URLs. "me" is derived from the TOKEN → there is nothing to tamper with → IDOR-proof by construction.
        g.MapGet("", async (HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var p = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == uid, ct)
                    ?? throw new ConflictException("PROFILE_NOT_READY", "Your profile is being set up. Try again in a moment.");   // eventual consistency, honestly surfaced
            return Results.Ok(new { p.UserId, p.Email, p.Phone, p.FullName, p.PreferredLanguage });
        });

        g.MapPut("", async (UpdateProfileRequest r, HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var p = await db.Customers.SingleOrDefaultAsync(x => x.UserId == uid, ct) ?? throw new NotFoundException();
            p.FullName = r.FullName.Trim();                     // only whitelisted fields are copied (mass-assignment defence)
            p.PreferredLanguage = r.PreferredLanguage;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithValidation<UpdateProfileRequest>();

        g.MapGet("/addresses", async (HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var list = await db.Addresses.AsNoTracking().Where(a => a.UserId == uid)
                .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ToListAsync(ct);
            return Results.Ok(list.Select(ToDto));
        });

        g.MapGet("/addresses/{id:guid}", async (Guid id, HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var a = await db.Addresses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct) ?? throw new NotFoundException();
            return Results.Ok(ToDto(a));
        });

        g.MapPost("/addresses", async (AddressRequest r, HttpContext c, UsersDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            if (!await db.Customers.AnyAsync(x => x.UserId == uid, ct))
            {
                throw new ConflictException("PROFILE_NOT_READY", "Your profile is being set up. Try again in a moment.");
            }
            if (await db.Addresses.CountAsync(a => a.UserId == uid, ct) >= 10)
            {
                throw new BusinessRuleException("ADDRESS_LIMIT", "You can save up to 10 addresses.");
            }
            PhoneNumber.TryNormalizeLk(r.Phone, out var phone);
            var isFirst = !await db.Addresses.AnyAsync(a => a.UserId == uid, ct);
            var a = new Address
            {
                Id = Guid.CreateVersion7(), UserId = uid, Label = r.Label, RecipientName = r.RecipientName.Trim(), Phone = phone,
                Line1 = r.Line1.Trim(), Line2 = r.Line2?.Trim(), City = r.City.Trim(), District = r.District, PostalCode = r.PostalCode,
                IsDefault = r.IsDefault || isFirst, CreatedAt = clock.GetUtcNow()
            };
            if (a.IsDefault)
            {
                await db.Addresses.Where(x => x.UserId == uid && x.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
            }
            db.Addresses.Add(a);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/customers/me/addresses/{a.Id}", ToDto(a));
        }).WithValidation<AddressRequest>();

        g.MapPut("/addresses/{id:guid}", async (Guid id, AddressRequest r, HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var a = await db.Addresses.SingleOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct) ?? throw new NotFoundException();   // ← the IDOR defence: filter by id AND owner; 404 if not mine
            PhoneNumber.TryNormalizeLk(r.Phone, out var phone);
            a.Label = r.Label; a.RecipientName = r.RecipientName.Trim(); a.Phone = phone; a.Line1 = r.Line1.Trim(); a.Line2 = r.Line2?.Trim();
            a.City = r.City.Trim(); a.District = r.District; a.PostalCode = r.PostalCode;
            if (r.IsDefault && !a.IsDefault)
            {
                await db.Addresses.Where(x => x.UserId == uid && x.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
                a.IsDefault = true;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(a));
        }).WithValidation<AddressRequest>();

        g.MapDelete("/addresses/{id:guid}", async (Guid id, HttpContext c, UsersDb db, CancellationToken ct) =>
        {
            var uid = c.User.UserId();
            var n = await db.Addresses.Where(x => x.Id == id && x.UserId == uid).ExecuteDeleteAsync(ct);
            return n == 0 ? throw new NotFoundException() : Results.NoContent();
        });
    }
}
