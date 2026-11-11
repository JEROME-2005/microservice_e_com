using LankaMart.BuildingBlocks;

namespace LankaMart.BuildingBlocks.Tests;

public class MoneyTests
{
    [Fact]
    public void Adds_without_losing_cents() =>
        Assert.Equal(new Money(125050), new Money(100000) + new Money(25050));

    [Fact]
    public void Multiplies_by_quantity() =>
        Assert.Equal(new Money(75000), new Money(25000) * 3);

    [Fact]
    public void Different_currencies_cannot_be_added() =>
        Assert.Throws<InvalidOperationException>(() => new Money(1, "LKR") + new Money(1, "USD"));

    [Fact]
    public void Overflow_throws_instead_of_wrapping() =>
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) * 2);
}

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0771234567", "+94771234567")]
    [InlineData("+94 77 123 4567", "+94771234567")]
    [InlineData("94771234567", "+94771234567")]
    public void Normalises_valid_numbers(string input, string expected)
    {
        Assert.True(PhoneNumber.TryNormalizeLk(input, out var e164));
        Assert.Equal(expected, e164);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("+4477123456")]
    [InlineData("07712345678")]
    public void Rejects_invalid_numbers(string input) => Assert.False(PhoneNumber.TryNormalizeLk(input, out _));
}

public class PagingAndLikeTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 100000, 1, 50)]
    [InlineData(3, 0, 3, 1)]
    public void Normalize_caps_page_size(int? page, int? size, int expectedPage, int expectedSize) =>
        Assert.Equal((expectedPage, expectedSize), Paging.Normalize(page, size));

    [Fact]
    public void Like_escapes_wildcards() => Assert.Equal("100\\%\\_off\\\\", Like.Escape("100%_off\\"));
}
