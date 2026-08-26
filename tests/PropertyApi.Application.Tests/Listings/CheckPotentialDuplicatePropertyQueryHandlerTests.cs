using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Queries.CheckPotentialDuplicateProperty;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Tests.Listings;

/// <summary>
/// Covers Phase-0 Task 4 — the advisory duplicate check must never throw and must
/// apply a wider tolerance to same-owner matches than to different-owner matches.
/// </summary>
public sealed class CheckPotentialDuplicatePropertyQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsNoMatches_WhenNeighborhoodIdIsMissing()
    {
        var handler = new CheckPotentialDuplicatePropertyQueryHandler(
            new StubPropertyRepository(Array.Empty<Property>()));

        var result = await handler.Handle(
            new CheckPotentialDuplicatePropertyQuery(Guid.NewGuid(), null, 100m, 500_000m, ListingType.ForSale),
            CancellationToken.None);

        Assert.False(result.HasPotentialDuplicates);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Handle_FlagsSameOwnerRepost_WithinTenPercentTolerance()
    {
        var ownerId = Guid.NewGuid();
        var neighborhoodId = 42;
        var existing = CreateProperty(ownerId, neighborhoodId, ListingType.ForSale, purchasePrice: 100_000m, area: 120m);

        var handler = new CheckPotentialDuplicatePropertyQueryHandler(
            new StubPropertyRepository(new[] { existing }));

        // 8% higher price — within the 10% same-owner tolerance.
        var result = await handler.Handle(
            new CheckPotentialDuplicatePropertyQuery(ownerId, neighborhoodId, 120m, 108_000m, ListingType.ForSale),
            CancellationToken.None);

        Assert.True(result.HasPotentialDuplicates);
        Assert.True(result.Candidates.Single().IsSameOwner);
    }

    [Fact]
    public async Task Handle_DoesNotFlag_DifferentOwner_WhenOutsideFivePercentTolerance()
    {
        var neighborhoodId = 42;
        var existing = CreateProperty(Guid.NewGuid(), neighborhoodId, ListingType.ForSale, purchasePrice: 100_000m, area: 120m);

        var handler = new CheckPotentialDuplicatePropertyQueryHandler(
            new StubPropertyRepository(new[] { existing }));

        // 8% higher price — outside the tighter 5% different-owner tolerance.
        var result = await handler.Handle(
            new CheckPotentialDuplicatePropertyQuery(Guid.NewGuid(), neighborhoodId, 120m, 108_000m, ListingType.ForSale),
            CancellationToken.None);

        Assert.False(result.HasPotentialDuplicates);
    }

    [Fact]
    public async Task Handle_FlagsDifferentOwner_WithinFivePercentTolerance()
    {
        var neighborhoodId = 42;
        var existing = CreateProperty(Guid.NewGuid(), neighborhoodId, ListingType.ForSale, purchasePrice: 100_000m, area: 120m);

        var handler = new CheckPotentialDuplicatePropertyQueryHandler(
            new StubPropertyRepository(new[] { existing }));

        // 3% higher price — within the 5% different-owner tolerance.
        var result = await handler.Handle(
            new CheckPotentialDuplicatePropertyQuery(Guid.NewGuid(), neighborhoodId, 120m, 103_000m, ListingType.ForSale),
            CancellationToken.None);

        Assert.True(result.HasPotentialDuplicates);
        Assert.False(result.Candidates.Single().IsSameOwner);
    }

    private static Property CreateProperty(
        Guid ownerId,
        int neighborhoodId,
        ListingType listingType,
        decimal? purchasePrice = null,
        decimal? area = null)
    {
        var property = Property.Create(
            title: "Existing listing",
            description: "Existing listing used as a duplicate-check fixture",
            ownerId: ownerId,
            listingType: listingType,
            countryCode: "SY",
            currencyCode: "SYP");

        property.NeighborhoodId = neighborhoodId;
        property.PurchasePrice = purchasePrice;
        property.Area = area;

        return property;
    }

    /// <summary>Minimal IPropertyRepository stub — only FindPotentialDuplicatesAsync is exercised here.</summary>
    private sealed class StubPropertyRepository : IPropertyRepository
    {
        private readonly IReadOnlyList<Property> _properties;

        public StubPropertyRepository(IReadOnlyList<Property> properties)
        {
            _properties = properties;
        }

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

        public Task<(int SoldCount, int RentedCount)> GetDealCountsByOwnerAsync(
            Guid ownerId,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<int> CountActiveListingsByOwnerAsync(
            Guid ownerId,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<int> CountActiveListingsByAgencyAsync(
            Guid agencyId,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Property>> FindPotentialDuplicatesAsync(
            int neighborhoodId,
            ListingType listingType,
            decimal? price,
            decimal? area,
            decimal maxTolerancePercent,
            CancellationToken ct = default)
            => Task.FromResult(_properties.Where(p => p.NeighborhoodId == neighborhoodId).ToList() as IReadOnlyList<Property>);

        public void Update(Property property) => throw new NotImplementedException();

        public void Remove(Property property) => throw new NotImplementedException();
    }
}
