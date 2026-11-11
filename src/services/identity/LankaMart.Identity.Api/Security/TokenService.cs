using System.Security.Cryptography;
using System.Text;
using LankaMart.Identity.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.Identity.Api.Security;

public sealed class TokenService(RsaKeyProvider keys, IOptions<JwtIssuingOptions> jwt, IOptions<AuthOptions> auth, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();
    public int AccessTokenSeconds => jwt.Value.AccessTokenMinutes * 60;

    public string CreateAccessToken(AppUser user, IList<string> roles)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["email"] = user.Email!,
            ["email_verified"] = user.EmailConfirmed,
            ["phone_verified"] = user.PhoneNumberConfirmed,
            ["role"] = roles.ToArray(),                          // array claim → several "role" claims on the other side
            ["jti"] = Guid.NewGuid().ToString("N"),              // unique id → lets us blacklist this exact token at logout
            ["amr"] = new[] { "pwd" }                            // how the user authenticated (MFA adds "otp" in Phase 3/6)
        };
        if (roles.Contains(Roles.Seller))
        {
            claims["seller_id"] = user.Id.ToString();   // seller id == user id, by design (the "tenant" claim)
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = auth.Value.Issuer,
            Audience = auth.Value.Audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(jwt.Value.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(keys.SigningKey, SecurityAlgorithms.RsaSha256)
        };
        return _handler.CreateToken(descriptor);
    }

    /// <summary>Opaque random refresh token. Returns the RAW value (sent to the client once) and the entity holding only its hash.</summary>
    public (string Raw, RefreshToken Entity) NewRefreshToken(Guid userId, Guid familyId, string? ip, string? userAgent)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));    // 256 bits of randomness
        var now = clock.GetUtcNow();
        return (raw, new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(jwt.Value.RefreshTokenDays),
            CreatedByIp = ip,
            UserAgent = userAgent?.Length > 300 ? userAgent[..300] : userAgent
        });
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
