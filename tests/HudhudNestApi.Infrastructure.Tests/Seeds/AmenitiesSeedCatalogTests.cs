using HudhudNestApi.Infrastructure.Persistence.Seeds;

namespace HudhudNestApi.Infrastructure.Tests.Seeds;

/// <summary>
/// The amenities reference list is what the listing forms, the search filter and the detail page
/// render. The Amenities table has a unique index on Name, so a duplicate (or over-long) catalog
/// entry would make the startup/Migrator seed fail for everyone — caught here instead.
/// </summary>
public sealed class AmenitiesSeedCatalogTests
{
    [Fact]
    public void Catalog_IsNotEmpty()
        => Assert.NotEmpty(AmenitiesSeed.Catalog);

    [Fact]
    public void Catalog_KeysAreUniqueLowerKebabCase()
    {
        var keys = AmenitiesSeed.Catalog.Select(a => a.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(keys, key => Assert.Matches("^[a-z]+(-[a-z]+)*$", key));
    }

    [Fact]
    public void Catalog_NamesAreUniqueAndFitTheColumn()
    {
        var names = AmenitiesSeed.Catalog.Select(a => a.NameAr).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(AmenitiesSeed.Catalog, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.NameAr));
            Assert.InRange(a.NameAr.Length, 1, 100);
            Assert.InRange(a.Category.Length, 1, 100);
            Assert.InRange(a.Key.Length, 1, 100);
        });
    }
}
