using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Valuation.Queries.GetComparableListings;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Valuation.Enums;
using Xunit;

namespace HudhudNestApi.Application.Tests.Valuation;

/// <summary>
/// Valuation Fast Path (Phase 3) — Handler-level tests. These exercise
/// GetComparableListingsQueryHandler's own decision/calculation logic (Min/Max/Average/
/// Median, Preliminary vs. Stage-4 handoff) against a hand-written IPropertyRepository stub
/// that returns a canned price list — same "no Moq, plain stub implementing the interface"
/// convention CheckPotentialDuplicatePropertyQueryHandlerTests already uses.
///
/// The actual DB-side matching RULES (PropertyType/Location/Area/ListingType/Active — the
/// ones this stub deliberately bypasses) are covered separately in
/// HudhudNestApi.Architecture.Tests/Persistence/ComparableListingsMatchingTests, because they
/// live in PropertyRepository (Infrastructure), which HudhudNestApi.Application.Tests
/// deliberately does not reference (see PropertySortOrderTests' own doc comment for why).
/// </summary>
public sealed class GetComparableListingsQueryHandlerTests
{
    private static readonly Guid InquiryId = Guid.NewGuid();

    private static GetComparableListingsQuery ValidQuery() => new(
        InquiryId: InquiryId,
        PropertyTypeId: 1,
        Area: 100m,
        GovernorateId: 1,
        DistrictId: 10,
        NeighborhoodId: 100,
        ListingType: ListingType.ForSale);

    private static GetComparableListingsQueryHandler BuildHandler(IReadOnlyList<decimal> prices)
        => new(
            new StubPropertyRepository(prices),
            NullLogger<GetComparableListingsQueryHandler>.Instance);

    // ── Count = 0 ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithNoComparables_ReturnsNoFabricatedValuation()
    {
        var handler = BuildHandler(Array.Empty<decimal>());

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.False(result.HasComparableListings);
        Assert.Equal(0, result.ComparableCount);
        Assert.Null(result.MinPrice);
        Assert.Null(result.MaxPrice);
        Assert.Null(result.AveragePrice);
        Assert.Null(result.MedianPrice);
        Assert.False(result.IsPreliminary);
        Assert.True(result.RequiresOfficeValuation);
        Assert.NotNull(result.Message);
    }

    // ── Count = 1 ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithOneComparable_UsesItForEveryStatistic()
    {
        var handler = BuildHandler([150_000m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(1, result.ComparableCount);
        Assert.Equal(150_000m, result.MinPrice);
        Assert.Equal(150_000m, result.MaxPrice);
        Assert.Equal(150_000m, result.AveragePrice);
        Assert.Equal(150_000m, result.MedianPrice);
        Assert.True(result.IsPreliminary);
        Assert.True(result.RequiresOfficeValuation);
    }

    // ── Count = 2 ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithTwoComparables_ComputesStatsAndStillRequiresOfficeValuation()
    {
        var handler = BuildHandler([100_000m, 120_000m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(2, result.ComparableCount);
        Assert.Equal(100_000m, result.MinPrice);
        Assert.Equal(120_000m, result.MaxPrice);
        Assert.Equal(110_000m, result.AveragePrice);
        Assert.Equal(110_000m, result.MedianPrice); // even count -> average of the two
        Assert.True(result.IsPreliminary);
        Assert.True(result.RequiresOfficeValuation);
    }

    // ── Count = 3 (exact threshold) ──────────────────────────────

    [Fact]
    public async Task Handle_WithThreeComparables_DoesNotRequireOfficeValuation()
    {
        var handler = BuildHandler([100m, 120m, 140m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(3, result.ComparableCount);
        Assert.Equal(100m, result.MinPrice);
        Assert.Equal(140m, result.MaxPrice);
        Assert.Equal(120m, result.AveragePrice);
        Assert.Equal(120m, result.MedianPrice);
        Assert.True(result.IsPreliminary);
        Assert.False(result.RequiresOfficeValuation);
    }

    // ── Count > 3 ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithFiveComparables_DoesNotAssumeExactlyThree()
    {
        var handler = BuildHandler([100m, 120m, 140m, 160m, 300m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(5, result.ComparableCount);
        Assert.Equal(100m, result.MinPrice);
        Assert.Equal(300m, result.MaxPrice);
        Assert.Equal(164m, result.AveragePrice);
        Assert.Equal(140m, result.MedianPrice); // odd count -> the middle value
        Assert.False(result.RequiresOfficeValuation);
    }

    // ── Median: odd vs. even count ────────────────────────────────

    [Fact]
    public async Task Handle_MedianOfOddCount_IsTheMiddleValue_NotTheAverage()
    {
        // Deliberately skewed (100, 120, 500) so Median and Average land on different
        // numbers — a symmetric set could pass even if Median were wrongly computed as Average.
        var handler = BuildHandler([500m, 100m, 120m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(120m, result.MedianPrice);
        Assert.Equal(240m, result.AveragePrice); // (500+100+120)/3
        Assert.NotEqual(result.AveragePrice, result.MedianPrice);
    }

    [Fact]
    public async Task Handle_MedianOfEvenCount_IsTheAverageOfTheTwoMiddleValues_NotTheOverallAverage()
    {
        // Deliberately skewed (100, 120, 140, 500) so Median and Average differ.
        var handler = BuildHandler([500m, 100m, 140m, 120m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(130m, result.MedianPrice); // (120 + 140) / 2
        Assert.Equal(215m, result.AveragePrice); // (100+120+140+500)/4
        Assert.NotEqual(result.AveragePrice, result.MedianPrice);
    }

    [Fact]
    public async Task Handle_MedianIsUnaffectedByInputOrder()
    {
        // The repository could return prices in any order — the handler must sort before
        // picking the middle value(s), not trust arrival order.
        var handler = BuildHandler([160m, 100m, 140m, 120m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(130m, result.MedianPrice);
        Assert.Equal(100m, result.MinPrice);
        Assert.Equal(160m, result.MaxPrice);
    }

    // ── Source / Disclaimer / Preliminary vs. Final ──────────────

    [Fact]
    public async Task Handle_WithComparables_AlwaysCarriesSourceAndDisclaimer()
    {
        var handler = BuildHandler([100m, 120m, 140m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Source));
        Assert.False(string.IsNullOrWhiteSpace(result.Disclaimer));
    }

    [Fact]
    public async Task Handle_NeverProducesAFinalValuation_RegardlessOfCount()
    {
        // Section 23: this phase's result is always Preliminary, never Final — even with
        // plenty of comparables. IsPreliminary is the only valuation-confidence flag; there
        // is no separate "IsFinal"/"Confirmed" flag anywhere on the result.
        var manyComparables = BuildHandler([100m, 120m, 140m, 160m, 180m, 200m]);
        var result = await manyComparables.Handle(ValidQuery(), CancellationToken.None);

        Assert.True(result.IsPreliminary);
    }

    // ── MatchLevel resolution from the query's own fields ────────

    [Fact]
    public async Task Handle_WithNeighborhoodId_ResolvesMatchLevelToNeighborhood()
    {
        var handler = BuildHandler([100m, 120m, 140m]);
        var query = ValidQuery() with { NeighborhoodId = 100, DistrictId = 10 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(ValuationMatchLevel.Neighborhood, result.MatchLevel);
    }

    [Fact]
    public async Task Handle_WithDistrictOnly_ResolvesMatchLevelToDistrict()
    {
        var handler = BuildHandler([100m, 120m, 140m]);
        var query = ValidQuery() with { NeighborhoodId = null, DistrictId = 10 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(ValuationMatchLevel.District, result.MatchLevel);
    }

    [Fact]
    public async Task Handle_WithGovernorateOnly_ResolvesMatchLevelToGovernorate()
    {
        var handler = BuildHandler([100m, 120m, 140m]);
        var query = ValidQuery() with { NeighborhoodId = null, DistrictId = null };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(ValuationMatchLevel.Governorate, result.MatchLevel);
    }

    // ── Defensive: an invalid inquiry must never throw ───────────

    [Fact]
    public async Task Handle_WithInvalidGovernorateId_ReturnsNoComparables_InsteadOfThrowing()
    {
        var handler = BuildHandler([100m, 120m, 140m]); // repository would never even be asked
        var query = ValidQuery() with { GovernorateId = 0 };

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.False(result.HasComparableListings);
        Assert.True(result.RequiresOfficeValuation);
    }

    [Fact]
    public async Task Handle_EchoesTheInquiryIdBackInTheResult()
    {
        var handler = BuildHandler([100m]);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        Assert.Equal(InquiryId, result.InquiryId);
    }

    /// <summary>Minimal IPropertyRepository stub — only GetComparableListingPricesAsync is exercised here.</summary>
    private sealed class StubPropertyRepository : IPropertyRepository
    {
        private readonly IReadOnlyList<decimal> _prices;

        public StubPropertyRepository(IReadOnlyList<decimal> prices)
        {
            _prices = prices;
        }

        public Task<IReadOnlyList<decimal>> GetComparableListingPricesAsync(
            int? propertyTypeId,
            ListingType listingType,
            decimal? area,
            decimal areaTolerancePercent,
            int governorateId,
            int? districtId,
            int? neighborhoodId,
            CancellationToken ct = default)
            => Task.FromResult(_prices);

        public Task AddAsync(Property property, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Property?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Property?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<PagedResult<Property>> GetPagedAsync(PropertyFilterDto filter, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Property>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> IsPubliclyVisibleAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<(int SoldCount, int RentedCount)> GetDealCountsByOwnerAsync(
            Guid ownerId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<int> CountActiveListingsByOwnerAsync(Guid ownerId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<int> CountActiveListingsByAgencyAsync(Guid agencyId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Property>> FindPotentialDuplicatesAsync(
            int neighborhoodId,
            ListingType listingType,
            decimal? price,
            decimal? area,
            decimal maxTolerancePercent,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public void Update(Property property) => throw new NotImplementedException();

        public void Remove(Property property) => throw new NotImplementedException();
    }
}
