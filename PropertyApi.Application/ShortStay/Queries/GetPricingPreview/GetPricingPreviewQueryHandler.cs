using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.ValueObjects;

namespace PropertyApi.Application.ShortStay.Queries.GetPricingPreview;

public sealed class GetPricingPreviewQueryHandler
    : IRequestHandler<GetPricingPreviewQuery, PricingBreakdownDto>
{
    private readonly IAccommodationUnitRepository _units;
    private readonly IPricingRuleRepository _pricingRules;
    private readonly IMinimumStayRuleRepository _minimumStayRules;
    private readonly IPricingCalculationService _pricing;

    public GetPricingPreviewQueryHandler(
        IAccommodationUnitRepository units,
        IPricingRuleRepository pricingRules,
        IMinimumStayRuleRepository minimumStayRules,
        IPricingCalculationService pricing)
    {
        _units = units;
        _pricingRules = pricingRules;
        _minimumStayRules = minimumStayRules;
        _pricing = pricing;
    }

    public async Task<PricingBreakdownDto> Handle(GetPricingPreviewQuery request, CancellationToken ct)
    {
        var unit = await _units.GetByIdWithListingAsync(request.UnitId, ct)
            ?? throw new NotFoundException($"Unit {request.UnitId} was not found.");

        var roomType = unit.RoomType;
        var listing = roomType.ShortStayListing;

        if (!listing.IsPublished)
            throw new DomainException("لا يمكن معاينة السعر لإعلان غير منشور.");

        var guests = GuestComposition.Create(request.Adults, request.Children, request.Infants);
        var capacity = roomType.Capacity ?? listing.Capacity;
        if (guests.CountedGuests > capacity)
            throw new DomainException($"عدد الضيوف يتجاوز السعة المسموحة ({capacity}) لهذه الوحدة.");

        var pricingRules = await _pricingRules.GetByRoomTypeIdAsync(roomType.Id, ct);
        var minimumStayRules = await _minimumStayRules.GetByRoomTypeIdAsync(roomType.Id, ct);

        return _pricing.Calculate(
            roomType, pricingRules, minimumStayRules,
            listing.CleaningFee, listing.ExtraGuestFee, listing.ExtraBedFee,
            listing.Capacity, request.CheckIn, request.CheckOut, guests.CountedGuests);
    }
}
