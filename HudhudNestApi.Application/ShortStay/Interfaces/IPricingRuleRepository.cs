using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Interfaces;

public interface IPricingRuleRepository
{
    Task<IReadOnlyList<PricingRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default);

    /// <summary>Replaces every existing rule for the room type with the given set — the host
    /// always resubmits the full rule set for a room type rather than patching individual rules,
    /// which keeps "which rule wins" unambiguous and matches how the editor UI naturally works.</summary>
    Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<PricingRule> rules, CancellationToken ct = default);
}

public interface IMinimumStayRuleRepository
{
    Task<IReadOnlyList<MinimumStayRule>> GetByRoomTypeIdAsync(Guid roomTypeId, CancellationToken ct = default);
    Task ReplaceForRoomTypeAsync(Guid roomTypeId, IReadOnlyList<MinimumStayRule> rules, CancellationToken ct = default);
}
