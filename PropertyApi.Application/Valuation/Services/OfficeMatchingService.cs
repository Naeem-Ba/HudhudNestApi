using Microsoft.Extensions.Logging;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.Services;

/// <summary>
/// Stage 4 — see <see cref="IOfficeMatchingService"/>. Searches for eligible agencies in
/// four progressively wider levels — Neighborhood, District, Governorate, then neighboring
/// Governorates — stopping as soon as <see cref="MinimumOfficeCoverage"/> distinct offices
/// are found, and never selecting the same agency twice.
///
/// Reuses <see cref="IAgencyRepository"/> (no new repository interface created) and
/// <see cref="IGovernorateNeighborProvider"/> (Stage 4's one new, explicitly-scoped
/// abstraction — see its own doc comment for why it exists and what it is not).
/// </summary>
public sealed class OfficeMatchingService : IOfficeMatchingService
{
    /// <summary>Minimum distinct offices Stage 5 needs before an inquiry has "enough" coverage.</summary>
    public const int MinimumOfficeCoverage = 3;

    private readonly IAgencyRepository _agencies;
    private readonly IGovernorateNeighborProvider _neighborGovernorates;
    private readonly ILogger<OfficeMatchingService> _logger;

    public OfficeMatchingService(
        IAgencyRepository agencies,
        IGovernorateNeighborProvider neighborGovernorates,
        ILogger<OfficeMatchingService> logger)
    {
        _agencies = agencies;
        _neighborGovernorates = neighborGovernorates;
        _logger = logger;
    }

    public async Task<OfficeMatchingResult> MatchOfficesAsync(
        Guid inquiryId,
        int governorateId,
        int? districtId,
        int? neighborhoodId,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        var selected = new List<OfficeMatch>();
        var selectedIds = new HashSet<Guid>();

        // Level 1 — Neighborhood (only when the inquiry actually has one).
        if (neighborhoodId.HasValue)
        {
            await AddLevelAsync(
                selected, selectedIds, inquiryId, utcNow,
                neighborhoodId: neighborhoodId, districtId: null, governorateIds: null,
                matchLevel: ValuationMatchLevel.Neighborhood, ct: ct);
        }

        // Level 2 — District (only when the inquiry has one; District rows already found at
        // Level 1 are excluded via selectedIds, so this only ever adds NEW agencies).
        if (selected.Count < MinimumOfficeCoverage && districtId.HasValue)
        {
            await AddLevelAsync(
                selected, selectedIds, inquiryId, utcNow,
                neighborhoodId: null, districtId: districtId, governorateIds: null,
                matchLevel: ValuationMatchLevel.District, ct: ct);
        }

        // Level 3 — the inquiry's own Governorate. Always attempted (GovernorateId is
        // mandatory on ValuationInquiry — see its Create() guard).
        if (selected.Count < MinimumOfficeCoverage)
        {
            await AddLevelAsync(
                selected, selectedIds, inquiryId, utcNow,
                neighborhoodId: null, districtId: null, governorateIds: [governorateId],
                matchLevel: ValuationMatchLevel.Governorate, ct: ct);
        }

        // Level 4 — neighboring Governorates. Never crosses into a neighbor's neighbor and
        // never invents adjacency — see IGovernorateNeighborProvider.
        if (selected.Count < MinimumOfficeCoverage)
        {
            var neighborIds = await _neighborGovernorates.GetNeighborIdsAsync(governorateId, ct);
            if (neighborIds.Count > 0)
            {
                await AddLevelAsync(
                    selected, selectedIds, inquiryId, utcNow,
                    neighborhoodId: null, districtId: null, governorateIds: neighborIds,
                    matchLevel: ValuationMatchLevel.GovernorateNeighboring, ct: ct);
            }
        }

        var hasMinimumCoverage = selected.Count >= MinimumOfficeCoverage;

        _logger.LogInformation(
            "Valuation office matching for inquiry {InquiryId}: found {OfficeCount} offices, " +
            "HasMinimumCoverage={HasMinimumCoverage}.",
            inquiryId, selected.Count, hasMinimumCoverage);

        // Explicit and final, per this stage's spec: an inquiry never comes out of matching
        // "still pending" — either it has enough coverage or InsufficientOfficeCoverage says
        // so plainly, right now, with whatever was actually found (0, 1, or 2 offices).
        return new OfficeMatchingResult
        {
            InquiryId = inquiryId,
            SelectedOffices = selected,
            HasMinimumCoverage = hasMinimumCoverage,
            InsufficientOfficeCoverage = !hasMinimumCoverage,
            MatchingCompleted = true
        };
    }

    private async Task AddLevelAsync(
        List<OfficeMatch> selected,
        HashSet<Guid> selectedIds,
        Guid inquiryId,
        DateTime utcNow,
        int? neighborhoodId,
        int? districtId,
        IReadOnlyCollection<int>? governorateIds,
        ValuationMatchLevel matchLevel,
        CancellationToken ct)
    {
        if (selected.Count >= MinimumOfficeCoverage)
            return;

        var agencies = await _agencies.FindActiveByLocationAsync(
            neighborhoodId, districtId, governorateIds, selectedIds, ct);

        foreach (var agency in agencies)
        {
            if (selected.Count >= MinimumOfficeCoverage)
                break;

            // Defensive duplicate guard (Application-logic half of Stage 4's anti-duplicate
            // requirement): FindActiveByLocationAsync already excludes selectedIds, but this
            // must hold even if a future repository change stops doing that.
            if (!selectedIds.Add(agency.Id))
                continue;

            selected.Add(new OfficeMatch
            {
                AgencyId = agency.Id,
                MatchLevel = matchLevel,
                Invitation = ValuationOfficeInvitation.Create(agency.Id, inquiryId, matchLevel, utcNow)
            });
        }
    }
}
