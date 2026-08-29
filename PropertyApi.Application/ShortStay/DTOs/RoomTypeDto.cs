namespace PropertyApi.Application.ShortStay.DTOs;

public sealed record AccommodationUnitDto(Guid Id, string Label, bool IsActive);

public sealed record PricingRuleDto(
    Guid Id,
    string RuleType,
    DayOfWeek? DayOfWeek,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    decimal PricePerNight);

public sealed record MinimumStayRuleDto(
    Guid Id,
    int MinimumNights,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd);

public sealed record RoomTypeDto(
    Guid Id,
    string Name,
    decimal BasePricePerNight,
    int? Capacity,
    bool IsActive,
    IReadOnlyList<AccommodationUnitDto> Units,
    IReadOnlyList<PricingRuleDto> PricingRules,
    IReadOnlyList<MinimumStayRuleDto> MinimumStayRules);
