using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.BuildingBlocks;

public sealed class AuthOptions
{
    public string Authority { get; set; } = "";                 // Identity service base URL (where the public keys live)
    public string Issuer { get; set; } = "lankamart-identity";  // must equal the 'iss' claim in tokens
    public string Audience { get; set; } = "lankamart-api";     // must equal the 'aud' claim
    public bool RequireHttps { get; set; } = true;              // false ONLY in local dev
}

public static class AuthExtensions
{
    public static IServiceCollection AddPlatformAuth(this IServiceCollection services, IConfiguration config)
    {
        var opt = config.GetSection("Auth").Get<AuthOptions>() ?? throw new InvalidOperationException("Missing 'Auth' configuration section.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = opt.Authority;                 // JwtBearer downloads public keys from {Authority}/.well-known/openid-configuration
            o.RequireHttpsMetadata = opt.RequireHttps;
            o.MapInboundClaims = false;                  // keep claim names as-is: "sub", "role" (not long Microsoft URIs)
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = opt.Issuer,
                ValidateAudience = true,
                ValidAudience = opt.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],   // blocks "alg:none" / algorithm-confusion attacks
                NameClaimType = "sub",
                RoleClaimType = "role"
            };
        });

        services.AddAuthorizationBuilder()
            // FAIL CLOSED (Principle #6): every endpoint requires a valid token unless it explicitly says .AllowAnonymous()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Customer, p => p.RequireRole(Roles.Customer))
            .AddPolicy(Policies.Seller, p => p.RequireRole(Roles.Seller))
            .AddPolicy(Policies.SellerAdmin, p => p.RequireRole(Roles.SellerManager, Roles.SuperAdmin))
            .AddPolicy(Policies.SuperAdmin, p => p.RequireRole(Roles.SuperAdmin));

        return services;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirstValue("sub"), out var id) ? id : throw new UnauthorizedException();

    public static Guid? TryUserId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirstValue("sub"), out var id) ? id : null;

    public static Guid SellerId(this ClaimsPrincipal u) =>
        Guid.TryParse(u.FindFirstValue("seller_id"), out var id) ? id : throw new ForbiddenException("NOT_A_SELLER", "Seller account required");

    public static bool HasFlag(this ClaimsPrincipal u, string claim) =>
        bool.TryParse(u.FindFirstValue(claim), out var v) && v;

    /// <summary>Phase-1 rule from System Scenarios §2.1: email AND phone must be verified before ordering.</summary>
    public static bool IsFullyVerified(this ClaimsPrincipal u) => u.HasFlag("email_verified") && u.HasFlag("phone_verified");
}
