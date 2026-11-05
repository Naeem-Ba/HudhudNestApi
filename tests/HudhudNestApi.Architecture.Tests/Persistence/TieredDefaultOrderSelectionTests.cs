using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Infrastructure.Repositories;

namespace HudhudNestApi.Architecture.Tests.Persistence;

/// <summary>
/// Which requests get the three-tier default list and which keep ApplySort. The rule mirrors the
/// branches of ApplySort that end in "featured first, then newest", so an explicit order the user
/// asked for (price, area, oldest first) is never replaced by the ranking.
/// </summary>
[Trait("Category", "Persistence")]
public sealed class TieredDefaultOrderSelectionTests
{
    [Fact]
    public void ThePlainList_UsesTheRanking()
        => Assert.True(PropertyRepository.UsesTieredDefaultOrder(new PropertyFilterDto()));

    [Theory]
    [InlineData("CreatedAt", true, true)]
    [InlineData("createdat", true, true)]
    [InlineData("CreatedAt", false, false)]     // oldest first is explicit
    [InlineData("PurchasePrice", true, false)]
    [InlineData("ColdRent", false, false)]
    [InlineData("Area", true, false)]
    public void ExplicitOrders_KeepTheirOwnSort(string sortBy, bool descending, bool tiered)
        => Assert.Equal(tiered, PropertyRepository.UsesTieredDefaultOrder(new PropertyFilterDto { SortBy = sortBy, SortDescending = descending }));

    [Theory]
    [InlineData(ListingType.ForSale, false)]    // cheapest/most expensive first
    [InlineData(ListingType.ForRent, false)]
    public void PriceOrder_NeedsASaleOrRentType_ToBeAnExplicitOrder(ListingType type, bool tiered)
        => Assert.Equal(tiered, PropertyRepository.UsesTieredDefaultOrder(new PropertyFilterDto { SortBy = "Price", ListingType = type }));

    [Fact]
    public void PriceOrder_WithoutAType_FallsBackToTheDefault_asApplySortDoes()
        => Assert.True(PropertyRepository.UsesTieredDefaultOrder(new PropertyFilterDto { SortBy = "Price" }));
}
