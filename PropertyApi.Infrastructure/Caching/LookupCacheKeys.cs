namespace PropertyApi.Infrastructure.Caching;

public static class LookupCacheKeys
{
    public const string Amenities = "amenities";
    public const string Categories = "categories";
    public const string PropertyTypes = "property-types";
    public const string Cities = "cities";

    public static readonly string[] All =
    [
        Amenities,
        Categories,
        PropertyTypes,
        Cities
    ];
}
