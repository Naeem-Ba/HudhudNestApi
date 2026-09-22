using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Queries.GetPricingPreview;
using HudhudNestApi.Application.ShortStay.Services;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.Tests.ShortStay.Queries;

public sealed class GetPricingPreviewQueryHandlerTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    private static (AccommodationUnit unit, ShortStayListing listing) CreatePublishedUnit(
        int capacity = 4, decimal basePrice = 100m)
    {
        var listing = ShortStayListing.Create(
            Guid.NewGuid(), 1, "Test Listing", "desc", capacity, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 0m, 0m);
        listing.UpdateBookingSettings(true, false);

        var roomType = new RoomType { Name = "Default", BasePricePerNight = basePrice, ShortStayListing = listing };
        var unit = new AccommodationUnit { Label = "Unit 1", RoomType = roomType };
        roomType.Units.Add(unit);
        listing.RoomTypes.Add(roomType);
        listing.Publish();

        return (unit, listing);
    }

    private static GetPricingPreviewQueryHandler MakeHandler(AccommodationUnit unit)
    {
        return new GetPricingPreviewQueryHandler(
            new StubUnitRepository(unit),
            new StubPricingRuleRepository([]),
            new StubMinimumStayRuleRepository([]),
            new PricingCalculationService());
    }

    [Fact]
    public async Task Handle_ValidRange_ReturnsBreakdownMatchingBasePrice()
    {
        var (unit, _) = CreatePublishedUnit(basePrice: 100m);
        var handler = MakeHandler(unit);

        var result = await handler.Handle(
            new GetPricingPreviewQuery(unit.Id, Today.AddDays(5), Today.AddDays(7), 2, 0, 0),
            CancellationToken.None);

        Assert.Equal(200m, result.TotalAmount);
        Assert.Equal(2, result.Nights.Count);
    }

    [Fact]
    public async Task Handle_Throws_WhenUnitNotFound()
    {
        var (unit, _) = CreatePublishedUnit();
        var handler = new GetPricingPreviewQueryHandler(
            new StubUnitRepository(null),
            new StubPricingRuleRepository([]),
            new StubMinimumStayRuleRepository([]),
            new PricingCalculationService());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GetPricingPreviewQuery(unit.Id, Today.AddDays(5), Today.AddDays(7), 2, 0, 0),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenGuestsExceedCapacity()
    {
        var (unit, _) = CreatePublishedUnit(capacity: 2);
        var handler = MakeHandler(unit);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new GetPricingPreviewQuery(unit.Id, Today.AddDays(5), Today.AddDays(7), 5, 0, 0),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenListingNotPublished()
    {
        var listing = ShortStayListing.Create(Guid.NewGuid(), 1, "T", "d", 4, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 0m, 0m);
        listing.UpdateBookingSettings(true, false);
        var roomType = new RoomType { Name = "Default", BasePricePerNight = 100m, ShortStayListing = listing };
        var unit = new AccommodationUnit { Label = "Unit 1", RoomType = roomType };
        roomType.Units.Add(unit);
        listing.RoomTypes.Add(roomType);
        // deliberately NOT published

        var handler = MakeHandler(unit);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new GetPricingPreviewQuery(unit.Id, Today.AddDays(5), Today.AddDays(7), 2, 0, 0),
            CancellationToken.None));
    }

    private sealed class StubUnitRepository : IAccommodationUnitRepository
    {
        private readonly AccommodationUnit? _unit;
        public StubUnitRepository(AccommodationUnit? unit) => _unit = unit;

        public Task AddAsync(AccommodationUnit unit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AccommodationUnit?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_unit != null && id == _unit.Id ? _unit : null);
        public Task<AccommodationUnit?> GetByIdWithListingAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_unit != null && id == _unit.Id ? _unit : null);
    }

    private sealed class StubPricingRuleRepository : IPricingRuleRepository
    {
        private readonly IReadOnlyList<PricingRule> _rules;
        public StubPricingRuleRepository(IReadOnlyList<PricingRule> rules) => _rules = rules;
        public Task<IReadOnlyList<PricingRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default) => Task.FromResult(_rules);
        public Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<PricingRule> rules, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubMinimumStayRuleRepository : IMinimumStayRuleRepository
    {
        private readonly IReadOnlyList<MinimumStayRule> _rules;
        public StubMinimumStayRuleRepository(IReadOnlyList<MinimumStayRule> rules) => _rules = rules;
        public Task<IReadOnlyList<MinimumStayRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default) => Task.FromResult(_rules);
        public Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<MinimumStayRule> rules, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
