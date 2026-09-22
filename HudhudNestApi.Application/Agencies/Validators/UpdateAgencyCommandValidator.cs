using FluentValidation;
using HudhudNestApi.Application.Agencies.Commands.UpdateAgency;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Application.Agencies.Validators;

/// <summary>
/// Same structured-location hierarchy guard as CreateAgencyCommandValidator, applied to
/// updates. Kept as a separate validator (rather than sharing one class between the two
/// commands) because FluentValidation validators are matched by request type, and
/// Create/UpdateAgencyCommand are different types — the two rule bodies are intentionally
/// identical in spirit and both delegate to ICommonLookupService.
/// </summary>
public sealed class UpdateAgencyCommandValidator : AbstractValidator<UpdateAgencyCommand>
{
    private readonly ICommonLookupService _lookups;

    public UpdateAgencyCommandValidator(ICommonLookupService lookups)
    {
        _lookups = lookups;

        RuleFor(x => x)
            .MustAsync(DistrictBelongsToGovernorateAsync)
            .WithMessage("المنطقة المحددة لا تنتمي إلى المحافظة المحددة.")
            .WithName("DistrictId");

        RuleFor(x => x)
            .MustAsync(NeighborhoodBelongsToDistrictAsync)
            .WithMessage("الحي المحدد لا ينتمي إلى المنطقة المحددة.")
            .WithName("NeighborhoodId");
    }

    private async Task<bool> DistrictBelongsToGovernorateAsync(
        UpdateAgencyCommand command, CancellationToken ct)
    {
        if (command.DistrictId is not { } districtId)
            return true;

        if (command.GovernorateId is not { } governorateId)
            return false;

        var districts = await _lookups.GetDistrictsAsync(governorateId, ct);
        return districts.Any(d => d.Id == districtId);
    }

    private async Task<bool> NeighborhoodBelongsToDistrictAsync(
        UpdateAgencyCommand command, CancellationToken ct)
    {
        if (command.NeighborhoodId is not { } neighborhoodId)
            return true;

        if (command.DistrictId is not { } districtId)
            return false;

        var neighborhoods = await _lookups.GetNeighborhoodsAsync(districtId, ct);
        return neighborhoods.Any(n => n.Id == neighborhoodId);
    }
}
