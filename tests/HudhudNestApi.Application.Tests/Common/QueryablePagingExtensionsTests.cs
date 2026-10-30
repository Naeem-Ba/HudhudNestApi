using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Application.Tests.Common;

/// <summary>Launch audit A2-005: (page - 1) * pageSize overflowed int and PostgreSQL got a negative OFFSET.</summary>
public sealed class QueryablePagingExtensionsTests
{
    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(3, 20, 40)]
    [InlineData(0, 20, 0)]
    [InlineData(-5, 20, 0)]
    [InlineData(int.MaxValue, 20, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void PageOffset_never_goes_negative(int page, int pageSize, int expected)
        => Assert.Equal(expected, QueryablePagingExtensions.PageOffset(page, pageSize));

    [Fact]
    public void SkipPage_on_a_huge_page_returns_nothing_instead_of_page_one()
    {
        var rows = Enumerable.Range(1, 50).AsQueryable();

        Assert.Empty(rows.SkipPage(int.MaxValue, 20));
        Assert.Equal(new[] { 21, 22 }, rows.SkipPage(2, 20).Take(2));
    }
}
