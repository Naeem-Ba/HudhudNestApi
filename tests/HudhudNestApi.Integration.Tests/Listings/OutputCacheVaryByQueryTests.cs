using System.Reflection;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Configuration;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Guards against the exact bug an audit found (2026-10): OutputCacheRegistration's
/// PublicPropertyListVaryByQueryParams silently missed 7 of PropertyFilterDto's bindable
/// properties (searchTerm, governorateId, districtId, neighborhoodId, propertyTypeId,
/// currencyCode, agencyId). SetVaryByQuery makes the cache key ignore any query param not in its
/// list, so two requests that differ only in one of those missing params collided on the same
/// cache entry -- one request's response served back for the other's filters, for up to 30s.
/// This reflects over PropertyFilterDto so a future added filter property fails this test until
/// its camelCase name is also added to PublicPropertyListVaryByQueryParams, rather than silently
/// reproducing the same class of bug.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class OutputCacheVaryByQueryTests
{
    [Fact]
    public void Every_bindable_PropertyFilterDto_property_has_a_vary_by_query_entry()
    {
        var expectedQueryNames = typeof(PropertyFilterDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToList();

        var actualQueryNames = OutputCacheRegistration.PublicPropertyListVaryByQueryParams;

        var missing = expectedQueryNames.Except(actualQueryNames, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "PropertyFilterDto gained a filter property with no matching entry in " +
            $"OutputCacheRegistration.PublicPropertyListVaryByQueryParams: {string.Join(", ", missing)}. " +
            "Without it, requests differing only in that param will be served each other's " +
            "cached responses.");
    }

    [Fact]
    public void Vary_by_query_list_has_no_stale_entries_PropertyFilterDto_no_longer_has()
    {
        var expectedQueryNames = typeof(PropertyFilterDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToHashSet(StringComparer.Ordinal);

        var actualQueryNames = OutputCacheRegistration.PublicPropertyListVaryByQueryParams;

        var stale = actualQueryNames.Where(name => !expectedQueryNames.Contains(name)).ToList();

        Assert.True(
            stale.Count == 0,
            "OutputCacheRegistration.PublicPropertyListVaryByQueryParams lists a query param " +
            $"PropertyFilterDto no longer has: {string.Join(", ", stale)}. Harmless for " +
            "correctness (an unused vary key just never varies), but likely dead from a rename " +
            "-- check whether the new property name needs the entry instead.");
    }
}
