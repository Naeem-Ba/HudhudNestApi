using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.DTOs;

/// <summary>One agency selected by Stage 4's OfficeMatchingService, and how precisely it was matched.</summary>
public sealed class OfficeMatch
{
    public Guid AgencyId { get; set; }

    public ValuationMatchLevel MatchLevel { get; set; }

    /// <summary>
    /// The invitation built in-memory for this match (via <see cref="ValuationOfficeInvitation.Create"/>,
    /// the Phase-2 factory) but NOT persisted here — see OfficeMatchingResult's own doc comment
    /// for why persistence is explicitly out of this stage's scope.
    /// </summary>
    public required ValuationOfficeInvitation Invitation { get; set; }
}

/// <summary>
/// Result of Stage 4's progressive geographic office matching for one <see cref="Domain.Valuation.Entities.ValuationInquiry"/>.
///
/// Mirrors the settable-property DTO style already used by
/// PropertyApi.Application.Valuation.DTOs.ComparableListingsResult (Stage 3) rather than
/// inventing a different shape for this module.
///
/// SCOPE NOTE — persistence: this phase's spec (section on OfficeMatchingService's result)
/// asks for the result to be "مناسبة مباشرة للمرحلة 5" (directly usable by Stage 5) and
/// mentions preventing a duplicate Invitation "before creating" one. There is, however, no
/// IValuationOfficeInvitationRepository, no EF Core configuration/DbSet, and no migration for
/// ValuationInquiry/ValuationOfficeInvitation anywhere in this codebase yet — Phase 2 left
/// all three Valuation entities deliberately unpersisted (Domain-only), and neither Stage 3
/// nor this stage's own required-file list (only OfficeMatchingService.cs) asks for that
/// infrastructure to be built. Building a repository/EF config/migration now would be
/// implementing persistence that no stage has explicitly requested, ahead of Stage 5 — which
/// is the stage that will actually need to query these rows back out of a store (its 24h SLA
/// hosted service has nothing to scan otherwise) and is better placed to decide that shape
/// deliberately. So: SelectedOffices below carries in-memory ValuationOfficeInvitation
/// objects (already duplicate-free — see MatchOfficesAsync), ready for whichever caller
/// Stage 5 introduces to persist them; this stage does not call any repository to save them.
/// Flagged again in the Stage 4 completion report's "Known limitations" section.
/// </summary>
public sealed class OfficeMatchingResult
{
    public Guid InquiryId { get; set; }

    public IReadOnlyList<OfficeMatch> SelectedOffices { get; set; } = Array.Empty<OfficeMatch>();

    public int Count => SelectedOffices.Count;

    /// <summary>True once 3 or more distinct offices were found (at any level(s) combined).</summary>
    public bool HasMinimumCoverage { get; set; }

    /// <summary>
    /// True when matching ran through every level — including neighboring governorates — and
    /// still found fewer than 3 offices. Never left ambiguous: this flag is always set to a
    /// definite true/false by the time MatchOfficesAsync returns, so nothing downstream can
    /// treat an inquiry as open-endedly "still matching".
    /// </summary>
    public bool InsufficientOfficeCoverage { get; set; }

    /// <summary>
    /// Always true today: OfficeMatchingService runs its full progressive-expansion pass
    /// synchronously and returns a final answer in one call — there is no partial/in-progress
    /// state to report. Kept as an explicit field (rather than only inferring "done" from the
    /// method having returned) because this phase's spec calls it out by name as part of the
    /// Result shape Stage 5 should be able to rely on.
    /// </summary>
    public bool MatchingCompleted { get; set; } = true;
}
