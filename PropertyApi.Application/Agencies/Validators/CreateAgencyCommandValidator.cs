using FluentValidation;
using PropertyApi.Application.Agencies.Commands.CreateAgency;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Application.Agencies.Validators;

/// <summary>
/// Guards the structured-location hierarchy on agency creation — GovernorateId/DistrictId/
/// NeighborhoodId are all optional (see CreateAgencyCommand's doc comments), but when a
/// child level is given it must actually belong to its parent. Reuses ICommonLookupService
/// (already backing the same check's absence on CreatePropertyCommandValidator — Property
/// has no equivalent rule today; this is the pattern Property's own lookups already support,
/// applied here because the Agency spec explicitly requires it) instead of querying
/// Districts/Neighborhoods directly, so there is exactly one place that knows how to resolve
/// a district's or neighborhood's parent.
/// </summary>
public sealed class CreateAgencyCommandValidator : AbstractValidator<CreateAgencyCommand>
{
    private readonly ICommonLookupService _lookups;

    public CreateAgencyCommandValidator(ICommonLookupService lookups)
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
        CreateAgencyCommand command, CancellationToken ct)
    {
        if (command.DistrictId is not { } districtId)
            return true; // No district given — nothing to check.

        // A district can never stand alone: it must be anchored under a governorate.
        if (command.GovernorateId is not { } governorateId)
            return false;

        var districts = await _lookups.GetDistrictsAsync(governorateId, ct);
        return districts.Any(d => d.Id == districtId);
    }

    private async Task<bool> NeighborhoodBelongsToDistrictAsync(
        CreateAgencyCommand command, CancellationToken ct)
    {
        if (command.NeighborhoodId is not { } neighborhoodId)
            return true; // No neighborhood given — nothing to check.

        // Same reasoning as the district rule above: a neighborhood depends on a district.
        if (command.DistrictId is not { } districtId)
            return false;

        var neighborhoods = await _lookups.GetNeighborhoodsAsync(districtId, ct);
        return neighborhoods.Any(n => n.Id == neighborhoodId);
    }
}
