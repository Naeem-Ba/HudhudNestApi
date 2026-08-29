using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Application.ShortStay.Commands.SetPricingRules;

public sealed record PricingRuleInput(
    string RuleType,
    DayOfWeek? DayOfWeek,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    decimal PricePerNight);

/// <summary>
/// Replaces the entire pricing-rule set for a RoomType in one call (see
/// IPricingRuleRepository.ReplaceForRoomTypeAsync for why: partial patches would make "which
/// rule wins" ambiguous across requests).
/// </summary>
public sealed record SetPricingRulesCommand(
    Guid RoomTypeId,
    Guid OwnerId,
    IReadOnlyList<PricingRuleInput> Rules) : IRequest<bool>;

public sealed class SetPricingRulesCommandHandler : IRequestHandler<SetPricingRulesCommand, bool>
{
    private readonly IRoomTypeRepository _roomTypes;
    private readonly IPricingRuleRepository _pricingRules;

    public SetPricingRulesCommandHandler(IRoomTypeRepository roomTypes, IPricingRuleRepository pricingRules)
    {
        _roomTypes = roomTypes;
        _pricingRules = pricingRules;
    }

    public async Task<bool> Handle(SetPricingRulesCommand request, CancellationToken ct)
    {
        var roomType = await _roomTypes.GetByIdAsync(request.RoomTypeId, ct)
            ?? throw new NotFoundException($"RoomType {request.RoomTypeId} was not found.");

        if (roomType.ShortStayListing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can set pricing rules.");

        var rules = request.Rules.Select(r => new PricingRule
        {
            RoomTypeId = roomType.Id,
            RuleType = Enum.Parse<PricingRuleType>(r.RuleType, ignoreCase: true),
            DayOfWeek = r.DayOfWeek,
            DateRangeStart = r.DateRangeStart,
            DateRangeEnd = r.DateRangeEnd,
            PricePerNight = r.PricePerNight,
        }).ToList();

        await _pricingRules.ReplaceForRoomTypeAsync(roomType.Id, rules, ct);
        return true;
    }
}
