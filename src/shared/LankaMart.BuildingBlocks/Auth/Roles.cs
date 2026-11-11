namespace LankaMart.BuildingBlocks;

/// <summary>The 9 actors from System Scenarios Part 1 (+ a reserved finance role, so Phase 5 needs no schema change).</summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string Seller = "seller";
    public const string Rider = "rider";
    public const string SellerManager = "seller_manager";
    public const string CustomerManager = "customer_manager";
    public const string RiderManager = "rider_manager";
    public const string AuditManager = "audit_manager";   // read-only everywhere (enforced in Phase 6)
    public const string SuperAdmin = "super_admin";
    public const string FinanceManager = "finance_manager"; // reserved for Phase 5

    public static readonly string[] All =
        [Customer, Seller, Rider, SellerManager, CustomerManager, RiderManager, AuditManager, SuperAdmin, FinanceManager];
}

public static class Policies
{
    public const string Customer = nameof(Customer);
    public const string Seller = nameof(Seller);
    public const string SellerAdmin = nameof(SellerAdmin);   // seller_manager OR super_admin
    public const string SuperAdmin = nameof(SuperAdmin);
}
