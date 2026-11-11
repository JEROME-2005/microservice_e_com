using LankaMart.Identity.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace LankaMart.Identity.Api.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IHostEnvironment env, IConfiguration cfg)
    {
        using var scope = sp.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        foreach (var r in Roles.All)                                    // all 9 actor roles exist from day one
        {
            if (!await roles.RoleExistsAsync(r))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(r) { Id = Guid.CreateVersion7() });
            }
        }

        if (!env.IsDevelopment())
        {
            return;                               // never auto-create admins outside dev
        }
        await EnsureAsync(users, cfg["Seed:SuperAdminEmail"], cfg["Seed:SuperAdminPassword"], "Super Admin", "+94700000001", Roles.SuperAdmin);
        await EnsureAsync(users, cfg["Seed:SellerManagerEmail"], cfg["Seed:SellerManagerPassword"], "Seller Manager", "+94700000002", Roles.SellerManager);
    }

    private static async Task EnsureAsync(UserManager<AppUser> um, string? email, string? pw, string name, string phone, string role)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pw) || await um.FindByEmailAsync(email) is not null)
        {
            return;
        }
        var u = new AppUser { Id = Guid.CreateVersion7(), UserName = email, Email = email, EmailConfirmed = true, PhoneNumber = phone, PhoneNumberConfirmed = true, FullName = name };
        if ((await um.CreateAsync(u, pw)).Succeeded)
        {
            await um.AddToRoleAsync(u, role);
        }
    }
}
