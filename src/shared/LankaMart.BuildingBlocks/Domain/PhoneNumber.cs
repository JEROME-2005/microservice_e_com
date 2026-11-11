namespace LankaMart.BuildingBlocks;

public static class PhoneNumber
{
    /// <summary>Normalises Sri Lankan numbers to E.164: 0771234567 / 94771234567 / +94 77 123 4567 → +94771234567.</summary>
    public static bool TryNormalizeLk(string? input, out string e164)
    {
        e164 = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        var s = new string(input.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (s.StartsWith("+94", StringComparison.Ordinal))
        {
            s = s[3..];
        }
        else if (s.StartsWith("94", StringComparison.Ordinal) && s.Length == 11)
        {
            s = s[2..];
        }
        else if (s.StartsWith('0'))
        {
            s = s[1..];
        }
        else
        {
            return false;
        }
        if (s.Length != 9 || !s.All(char.IsDigit))
        {
            return false;
        }
        e164 = "+94" + s;
        return true;
    }
}
