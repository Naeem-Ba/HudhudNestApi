namespace PropertyApi.Infrastructure.Caching;

public static class LookupCacheKeys
{
    public const string Amenities = "amenities";
    public const string Categories = "categories";
    public const string PropertyTypes = "property-types";
    public const string Cities = "cities";

    // Phase-0, Task 0 follow-up. Governorates and the PropertyType catalog are
    // small, rarely-changing sets, so they follow the same fixed-key caching
    // pattern as the entries above. Districts/Neighborhoods are NOT listed here
    // deliberately: they are scoped by parent id (governorateId/districtId), so
    // a single fixed cache key can't represent "all of them" — see
    // CommonLookupService.GetDistrictsAsync/GetNeighborhoodsAsync, which build a
    // parent-scoped key per call instead and are excluded from RefreshAllAsync.
    public const string Governorates = "governorates";
    public const string PropertyTypeCatalog = "property-type-catalog";

    public static readonly string[] All =
    [
        Amenities,
        Categories,
        PropertyTypes,
        Cities,
        Governorates,
        PropertyTypeCatalog
    ];
}
