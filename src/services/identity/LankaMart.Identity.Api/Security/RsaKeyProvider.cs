using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.Identity.Api.Security;

public sealed class JwtIssuingOptions
{
    public string KeyPath { get; set; } = "keys/jwt-private.pem";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>Loads the RSA private key from a PEM file, creating it on first run (DEV ONLY — production injects it from Vault / a K8s secret).
/// Tokens are signed with the PRIVATE key (only Identity has it) and verified with the PUBLIC key (everyone can fetch it).</summary>
public sealed class RsaKeyProvider
{
    public RsaSecurityKey SigningKey { get; }
    public RSAParameters PublicParameters { get; }
    public string KeyId => SigningKey.KeyId!;

    public RsaKeyProvider(IOptions<JwtIssuingOptions> opt, ILogger<RsaKeyProvider> log)
    {
        var path = opt.Value.KeyPath;
        var rsa = RSA.Create(2048);
        if (File.Exists(path))
        {
            rsa.ImportFromPem(File.ReadAllText(path));
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
            log.LogWarning("Generated a NEW JWT signing key at {Path}. Never commit it (it's in .gitignore).", path);
        }
        PublicParameters = rsa.ExportParameters(false);
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(PublicParameters.Modulus!))[..16];   // key id lets you rotate keys later
        SigningKey = new RsaSecurityKey(rsa) { KeyId = kid };
    }
}
