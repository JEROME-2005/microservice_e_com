using Microsoft.AspNetCore.Identity;

namespace LankaMart.Identity.Api.Domain;

public class AppUser : IdentityUser<Guid>      // gives us: Email, PhoneNumber, EmailConfirmed, PhoneNumberConfirmed, lockout fields, password hash
{
    public string FullName { get; set; } = "";
    public string Language { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }            // every token descended from one login shares a family
    public string TokenHash { get; set; } = "";   // SHA-256 of the token — the raw token is NEVER stored (DB leak ≠ session theft)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
}
