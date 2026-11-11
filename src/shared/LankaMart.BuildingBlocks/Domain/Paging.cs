namespace LankaMart.BuildingBlocks;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public static class Paging
{
    /// <summary>Caps page size (API4: unrestricted resource consumption — never trust ?page_size=100000).</summary>
    public static (int page, int size) Normalize(int? page, int? pageSize, int max = 50, int def = 20)
        => (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? def, 1, max));
}
