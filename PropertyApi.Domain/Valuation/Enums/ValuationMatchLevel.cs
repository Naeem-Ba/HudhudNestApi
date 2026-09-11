namespace PropertyApi.Domain.Valuation.Enums;

/// <summary>
/// How precisely a <see cref="Entities.ValuationOfficeInvitation"/>'s office was matched to
/// its inquiry's location — recorded for later analysis, not computed here. The matching
/// algorithm itself (comparing Agency.GovernorateId/DistrictId/NeighborhoodId against
/// ValuationInquiry's own location fields) belongs to the Application layer in a later
/// phase; this Domain layer only stores whichever level that algorithm decided on.
///
/// Ordered narrowest-first to mirror the Governorate → District → Neighborhood priority
/// used throughout this codebase's location model (see Property/Agency's own structured
/// location fields) — Neighborhood is the most precise match, Governorate the least.
///
/// <see cref="GovernorateNeighboring"/> was appended in Stage 4 (Office Matching) for the
/// case where fewer than 3 offices exist even within the inquiry's own governorate and the
/// search must expand into a bordering governorate. Appended rather than inserted, so the
/// three original ordinals (0/1/2) are unchanged for anything already persisted or compared
/// by value.
/// </summary>
public enum ValuationMatchLevel
{
    Neighborhood = 0,
    District = 1,
    Governorate = 2,
    GovernorateNeighboring = 3,
}
