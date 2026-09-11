namespace PropertyApi.Application.Valuation.Interfaces;

/// <summary>
/// Resolves which governorates border a given one — Stage 4's Level 4 fallback, used only
/// when fewer than 3 active agencies were found even at the Governorate level.
///
/// INSPECTION NOTE (Stage 4): no geographic-adjacency data exists anywhere in this project
/// today. <c>Governorate</c> (PropertyApi.Domain/Lookups/Entities/Governorate.cs) carries
/// only Id/NameAr/NameEn/CountryCode/SortOrder/IsActive — SortOrder is a display order used
/// by <c>CommonLookupService.GetGovernoratesAsync</c>, not a geographic relation. There is no
/// PostGIS, no coordinates/Latitude/Longitude anywhere in the Domain, and no
/// neighbor/adjacency table. Building a full GIS/distance system purely for this one
/// fallback tier would be disproportionate (explicitly forbidden by this phase's spec), so
/// this is the minimal named seam it asks for instead: implemented today as a fixed,
/// real-world adjacency list (Syria's well-known governorate borders, the same "authoritative
/// reference, not invented" standard <see cref="Infrastructure.Lookups.GovernorateNeighborProvider"/>
/// documents), swappable later for a DB-backed or PostGIS implementation without touching any
/// caller.
/// </summary>
public interface IGovernorateNeighborProvider
{
    /// <summary>
    /// The ids of governorates that share a border with <paramref name="governorateId"/>.
    /// Never includes <paramref name="governorateId"/> itself. Returns an empty list — never
    /// throws — for an unknown id, so a bad/foreign id can't fail Stage 4's matching pass.
    /// </summary>
    Task<IReadOnlyList<int>> GetNeighborIdsAsync(int governorateId, CancellationToken ct = default);
}
