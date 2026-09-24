namespace PropertyApi.Application.Common.Interfaces;

/// <summary>
/// Whether the structured location ids sent with a listing belong together (district inside the governorate,
/// neighborhood inside the district). Nothing checked this, so a listing could be saved under one governorate
/// with another governorate's district and then show up in the wrong filter results.
/// Checks the catalog regardless of IsActive: a listing saved before a location was deactivated must still be
/// editable with its existing ids.
/// </summary>
public interface ILocationHierarchyChecker
{
    /// <summary>
    /// True when every pair that is present is consistent. A missing level (null id, e.g. a typed DistrictText)
    /// is not checked; unknown ids are reported as inconsistent.
    /// </summary>
    Task<bool> IsConsistentAsync(int? governorateId, int? districtId, int? neighborhoodId, CancellationToken ct = default);
}
