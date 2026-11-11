namespace LankaMart.Users.Api.Domain;

public class CustomerProfile
{
    public Guid UserId { get; set; }                  // same id as the Identity user
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PreferredLanguage { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; }
    public List<Address> Addresses { get; set; } = [];
}

public class Address
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Label { get; set; } = "Home";
    public string RecipientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string City { get; set; } = "";
    public string District { get; set; } = "";
    public string? PostalCode { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum SellerStatus { Registered, KycSubmitted, UnderReview, Active, Rejected, Suspended }

public sealed record KycData(string BusinessName, string BusinessType, string? BrNumber, string GoodsCategory, string NicNumber,
                             string AddressLine, string District, string BankName, string BankBranch, string BankAccountName, string BankAccountNumber);

/// <summary>The Seller Onboarding State Machine (architecture doc §4.2):
///   Registered → KycSubmitted → UnderReview → Active ⇄ Suspended ;   KycSubmitted/UnderReview → Rejected → (resubmit) KycSubmitted
/// Why a state machine and not a bool "approved"? Suspended sellers keep their history but can't sell; a boolean can't say that.</summary>
public class SellerProfile
{
    public Guid UserId { get; private set; }          // == seller id platform-wide
    public string Email { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public SellerStatus Status { get; private set; } = SellerStatus.Registered;

    // KYC form data (document uploads arrive in Phase 3 with the Media service)
    public string? BusinessName { get; private set; }
    public string? BusinessType { get; private set; }
    public string? BrNumber { get; private set; }
    public string? GoodsCategory { get; private set; }
    public string? NicNumber { get; private set; }             // TODO Phase 6: encrypt at rest (AES-GCM, key in Vault)
    public string? AddressLine { get; private set; }
    public string? District { get; private set; }
    public string? BankName { get; private set; }
    public string? BankBranch { get; private set; }
    public string? BankAccountName { get; private set; }
    public string? BankAccountNumber { get; private set; }     // TODO Phase 6: encrypt

    public int KycAttempts { get; private set; }
    public string? RejectionReasonCode { get; private set; }
    public string? RejectionNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }

    public static SellerProfile Create(Guid userId, string name, string email, string phone, DateTimeOffset now) =>
        new() { UserId = userId, DisplayName = name, Email = email, Phone = phone, CreatedAt = now };

    private void Require(string action, params SellerStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new BusinessRuleException("INVALID_SELLER_STATE", $"Cannot {action} while seller is {Status}.");
        }
    }

    public void SubmitKyc(KycData d, DateTimeOffset now)
    {
        Require("submit KYC", SellerStatus.Registered, SellerStatus.Rejected);          // resubmission allowed only after rejection (unlimited attempts, counted)
        BusinessName = d.BusinessName; BusinessType = d.BusinessType; BrNumber = d.BrNumber; GoodsCategory = d.GoodsCategory;
        NicNumber = d.NicNumber; AddressLine = d.AddressLine; District = d.District;
        BankName = d.BankName; BankBranch = d.BankBranch; BankAccountName = d.BankAccountName; BankAccountNumber = d.BankAccountNumber;
        KycAttempts++;
        SubmittedAt = now;
        RejectionReasonCode = RejectionNote = null;
        Status = SellerStatus.KycSubmitted;
    }

    public void StartReview(Guid by)
    {
        Require("start review", SellerStatus.KycSubmitted);
        Status = SellerStatus.UnderReview;
        DecidedBy = by;
    }

    public void Approve(Guid by, DateTimeOffset now)
    {
        Require("approve", SellerStatus.KycSubmitted, SellerStatus.UnderReview);
        Status = SellerStatus.Active;
        Decide(by, now);
    }

    public void Reject(Guid by, string code, string? note, DateTimeOffset now)
    {
        Require("reject", SellerStatus.KycSubmitted, SellerStatus.UnderReview);
        Status = SellerStatus.Rejected;
        RejectionReasonCode = code;
        RejectionNote = note;
        Decide(by, now);
    }

    public void Suspend(Guid by, DateTimeOffset now)
    {
        Require("suspend", SellerStatus.Active);
        Status = SellerStatus.Suspended;
        Decide(by, now);
    }

    public void Reinstate(Guid by, DateTimeOffset now)
    {
        Require("reinstate", SellerStatus.Suspended);
        Status = SellerStatus.Active;
        Decide(by, now);
    }

    private void Decide(Guid by, DateTimeOffset now)
    {
        DecidedBy = by;
        DecidedAt = now;
    }
}
