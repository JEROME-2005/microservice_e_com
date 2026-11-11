namespace LankaMart.BuildingBlocks;

/// <summary>Money as integer minor units (cents). NEVER float/double: 0.1 + 0.2 != 0.3 is how platforms lose real money.
/// LKR 1,250.50 is stored as 125050.</summary>
public readonly record struct Money(long Cents, string Currency = "LKR")
{
    public static Money Zero(string currency = "LKR") => new(0, currency);
    public static Money operator +(Money a, Money b) { Same(a, b); return new(checked(a.Cents + b.Cents), a.Currency); }
    public static Money operator -(Money a, Money b) { Same(a, b); return new(checked(a.Cents - b.Cents), a.Currency); }
    public static Money operator *(Money a, int qty) => new(checked(a.Cents * qty), a.Currency);   // checked = throw on overflow instead of wrapping silently
    private static void Same(Money a, Money b)
    {
        if (a.Currency != b.Currency)
        {
            throw new InvalidOperationException("Currency mismatch");
        }
    }
    public override string ToString() => $"{Currency} {Cents / 100m:N2}";   // display only
}
