using HudhudNestApi.Application.Properties.Ranking;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// The page arithmetic behind the default listing list: featured listings are capped per page and
/// every listing, featured or not, is reached exactly once when paging through.
/// </summary>
public sealed class FeaturedPageLayoutTests
{
    [Theory]
    [InlineData(6, 5, 2)]    // a six-item home section carries at most two ads
    [InlineData(20, 5, 5)]
    [InlineData(100, 5, 5)]  // the configured maximum wins on a big page
    [InlineData(1, 5, 1)]
    [InlineData(20, 3, 3)]
    [InlineData(20, 0, 0)]   // 0 switches featured placement off
    public void CapFor_isAQuarterOfThePage_neverMoreThanTheConfiguredMaximum(int pageSize, int configured, int expected)
        => Assert.Equal(expected, FeaturedPageLayout.CapFor(pageSize, configured));

    /// <summary>Walks every page and rebuilds the two sequences from the slices.</summary>
    private static (List<int> Featured, List<int> Ordinary, List<FeaturedPageLayout.Slice> Pages) Walk(
        int pageSize, int featured, int ordinary, int configured)
    {
        var f = new List<int>();
        var o = new List<int>();
        var pages = new List<FeaturedPageLayout.Slice>();
        var total = featured + ordinary;
        var lastPage = Math.Max(1, (total + pageSize - 1) / pageSize);
        for (var page = 1; page <= lastPage; page++)
        {
            var slice = FeaturedPageLayout.Plan(page, pageSize, featured, ordinary, configured);
            pages.Add(slice);
            f.AddRange(Enumerable.Range(slice.FeaturedSkip, slice.FeaturedTake));
            o.AddRange(Enumerable.Range(slice.OrdinarySkip, slice.OrdinaryTake));
        }
        return (f, o, pages);
    }

    [Fact]
    public void PagingThroughEverything_returnsEachListingExactlyOnce_inOrder_forManyShapes()
    {
        foreach (var pageSize in new[] { 1, 2, 3, 6, 7, 20, 25, 100 })
            foreach (var featured in new[] { 0, 1, 4, 5, 6, 11, 37, 300 })
                foreach (var ordinary in new[] { 0, 1, 5, 19, 20, 21, 99, 250 })
                    foreach (var configured in new[] { 0, 1, 5, 50 })
                    {
                        var (f, o, _) = Walk(pageSize, featured, ordinary, configured);

                        // No duplicates and no gaps: the slices tile the two sequences.
                        Assert.Equal(Enumerable.Range(0, featured), f);
                        Assert.Equal(Enumerable.Range(0, ordinary), o);
                    }
    }

    [Fact]
    public void EveryPageIsFull_exceptTheLast_andNeverHasMoreThanTheCap_whileOrdinaryListingsRemain()
    {
        foreach (var pageSize in new[] { 1, 3, 6, 20, 100 })
            foreach (var featured in new[] { 0, 1, 7, 40, 300 })
                foreach (var ordinary in new[] { 0, 3, 50, 400 })
                {
                    const int configured = 5;
                    var cap = FeaturedPageLayout.CapFor(pageSize, configured);
                    var total = featured + ordinary;
                    var (_, _, pages) = Walk(pageSize, featured, ordinary, configured);

                    for (var i = 0; i < pages.Count; i++)
                    {
                        var items = pages[i].FeaturedTake + pages[i].OrdinaryTake;
                        var isLast = i == pages.Count - 1;
                        Assert.Equal(isLast ? total - (pages.Count - 1) * pageSize : pageSize, items);

                        var ordinaryLeft = ordinary - pages[i].OrdinarySkip;
                        if (ordinaryLeft > pageSize - cap)
                            Assert.True(pages[i].FeaturedTake <= cap, $"page {i + 1}: {pages[i].FeaturedTake} featured > cap {cap}");
                    }
                }
    }

    [Fact]
    public void WithNoCap_FeaturedListingsAreOnlyUsedToFillAPage_afterTheOrdinaryOnesRunOut()
    {
        // configured 0: ordinary listings first, the featured ones only fill what is left.
        var (f, o, pages) = Walk(pageSize: 10, featured: 4, ordinary: 12, configured: 0);

        Assert.Equal(Enumerable.Range(0, 4), f);
        Assert.Equal(Enumerable.Range(0, 12), o);
        Assert.Equal(0, pages[0].FeaturedTake);
        Assert.Equal(4, pages[1].FeaturedTake);
    }

    [Fact]
    public void ThePageOfTheHomeSection_showsTwoAdsAndFourOrdinaryListings()
    {
        var slice = FeaturedPageLayout.Plan(page: 1, pageSize: 6, featuredCount: 300, ordinaryCount: 99_700, configuredMax: 5);

        Assert.Equal(new FeaturedPageLayout.Slice(0, 2, 0, 4), slice);
    }

    [Fact]
    public void AdsBeyondThePageCap_leadTheNextPage_insteadOfBeingDemoted()
    {
        var page1 = FeaturedPageLayout.Plan(1, 20, 12, 100, 5);
        var page2 = FeaturedPageLayout.Plan(2, 20, 12, 100, 5);
        var page3 = FeaturedPageLayout.Plan(3, 20, 12, 100, 5);

        Assert.Equal((0, 5), (page1.FeaturedSkip, page1.FeaturedTake));
        Assert.Equal((5, 5), (page2.FeaturedSkip, page2.FeaturedTake));
        Assert.Equal((10, 2), (page3.FeaturedSkip, page3.FeaturedTake));
        Assert.Equal((15, 15), (page2.OrdinarySkip, page2.OrdinaryTake)); // page 2 starts after page 1's 15 ordinary
    }

    [Fact]
    public void AnEmptyResult_andOutOfRangeInput_areHarmless()
    {
        Assert.Equal(default, FeaturedPageLayout.Plan(1, 20, 0, 0, 5));
        var beyond = FeaturedPageLayout.Plan(9, 20, 3, 4, 5);                      // beyond the last page: nothing to take
        Assert.Equal((0, 0), (beyond.FeaturedTake, beyond.OrdinaryTake));
        Assert.Equal(FeaturedPageLayout.Plan(1, 1, 3, 4, 5), FeaturedPageLayout.Plan(-4, 0, 3, 4, 5));
    }
}
