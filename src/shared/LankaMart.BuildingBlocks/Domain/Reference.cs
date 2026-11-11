namespace LankaMart.BuildingBlocks;

public static class SriLanka
{
    public static readonly string[] Districts =
    [
        "Ampara","Anuradhapura","Badulla","Batticaloa","Colombo","Galle","Gampaha","Hambantota","Jaffna","Kalutara","Kandy","Kegalle","Kilinochchi",
        "Kurunegala","Mannar","Matale","Matara","Monaragala","Mullaitivu","Nuwara Eliya","Polonnaruwa","Puttalam","Ratnapura","Trincomalee","Vavuniya"
    ];
}

public static class Lang
{
    public static readonly string[] Supported = ["en", "si", "ta"];
    public static string Normalize(string? l) => l is not null && Supported.Contains(l.ToLowerInvariant()) ? l.ToLowerInvariant() : "en";
}

public static class Like
{
    /// <summary>Escape user text for use inside ILIKE with escape char '\'. Without this a search for "%" matches everything.</summary>
    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
