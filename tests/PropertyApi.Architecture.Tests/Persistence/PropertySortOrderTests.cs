using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Repositories;

namespace PropertyApi.Architecture.Tests.Persistence;

/// <summary>
/// The search ordering, including paid featured placement.
///
/// This suite exists because of a defect, not a feature request: IsFeatured was written by the
/// payment path and read by no query, no sort, and no DTO. An owner paid $5.00 for a placement,
/// the flag was set for thirty days, and the listing appeared in exactly the same position it
/// had before — money taken for a product that was not delivered. There was no test for the
/// behaviour because there was no behaviour.
///
/// It lives in this project because PropertyApi.Application.Tests deliberately does not
/// reference Infrastructure, and PropertyRepository does. Persistence/MigrationCompletenessTests
/// is here for the same reason.
///
/// ApplySort is exercised over LINQ-to-Objects rather than a database. That checks the rule, not
/// its SQL translation — the translation is covered by the explicit null guard in
/// Property.CurrentlyFeatured and reviewed against the generated SQL.
/// </summary>
[Trait("Category", "Persistence")]
public sealed class PropertySortOrderTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DefaultOrder_PutsAFeaturedListingAboveANewerUnfeaturedOne()
    {
        // The exact case an owner pays for: their listing is older, so newest-first would
        // bury it. Without the fix this assertion fails — the featured listing stays second.
        var featured = Listing("featured", createdDaysAgo: 10, featuredFrom: Now.AddDays(-1));
        var newer = Listing("newer", createdDaysAgo: 1);

        var ordered = Sort([newer, featured], sortBy: null, descending: true);

        Assert.Equal(["featured", "newer"], ordered);
    }

    [Fact]
    public void DefaultOrder_StillSortsNewestFirstAmongUnfeaturedListings()
    {
        var older = Listing("older", createdDaysAgo: 10);
        var newer = Listing("newer", createdDaysAgo: 1);

        var ordered = Sort([older, newer], sortBy: null, descending: true);

        Assert.Equal(["newer", "older"], ordered);
    }

    [Fact]
    public void DefaultOrder_DoesNotPromoteAListingWhosePaidWindowHasElapsed()
    {
        // Flag still true, window over, sweep has not run yet. Nobody paid for these days.
        var lapsed = Listing("lapsed", createdDaysAgo: 10, featuredFrom: Now.AddDays(-31));
        var newer = Listing("newer", createdDaysAgo: 1);

        var ordered = Sort([lapsed, newer], sortBy: null, descending: true);

        Assert.Equal(["newer", "lapsed"], ordered);
    }

    [Fact]
    public void ExplicitCreatedAtAscending_AlsoPromotesFeatured()
    {
        // "Oldest first" is still the default ordering, just reversed — the client sends
        // SortBy=CreatedAt for it, so it must behave like the no-sort case.
        var featured = Listing("featured", createdDaysAgo: 1, featuredFrom: Now.AddDays(-1));
        var older = Listing("older", createdDaysAgo: 10);

        var ordered = Sort([older, featured], sortBy: "CreatedAt", descending: false);

        Assert.Equal(["featured", "older"], ordered);
    }

    [Theory]
    [InlineData("PurchasePrice")]
    [InlineData("ColdRent")]
    [InlineData("Area")]
    public void ExplicitSort_IsNotDisturbedByFeaturedPlacement(string sortBy)
    {
        // A paid placement must never make "cheapest first" untrue. If it could, the sort
        // control would be lying to the user, and the listing that outranks everything is the
        // one that paid rather than the one that matches.
        var featuredExpensive = Listing(
            "expensive", createdDaysAgo: 10, featuredFrom: Now.AddDays(-1), price: 900m);
        var cheap = Listing("cheap", createdDaysAgo: 1, price: 100m);

        var ordered = Sort([featuredExpensive, cheap], sortBy, descending: false);

        Assert.Equal(["cheap", "expensive"], ordered);
    }

    [Fact]
    public void SortKeyIsCaseInsensitive()
    {
        var featured = Listing(
            "featured", createdDaysAgo: 10, featuredFrom: Now.AddDays(-1), price: 900m);
        var cheap = Listing("cheap", createdDaysAgo: 1, price: 100m);

        // The Angular client sends "PurchasePrice"; older callers send "purchaseprice".
        var ordered = Sort([featured, cheap], "purchaseprice", descending: false);

        Assert.Equal(["cheap", "featured"], ordered);
    }

    [Fact]
    public void FeaturedSortKey_TranslatesToSqlThatCannotYieldNull()
    {
        // The hazard this guards: if the generated ORDER BY key could evaluate to NULL, a row
        // with IsFeatured = true and FeaturedUntil = NULL would sort FIRST under PostgreSQL's
        // "DESC means NULLS FIRST" default — promoting exactly the rows nobody paid for, on
        // production data only, silently. The explicit IS NOT NULL in Property.CurrentlyFeatured
        // is what prevents it, so its survival into the SQL is asserted rather than assumed.
        //
        // Offline: ToQueryString needs a provider, never a connection.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=sql_shape_only;Username=none;Password=none")
            .Options;

        using var db = new AppDbContext(options);

        var sql = PropertyRepository
            .ApplySort(db.Properties, new PropertyFilterDto(), Now)
            .ToQueryString();

        var orderBy = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];

        Assert.Contains("\"FeaturedUntil\" IS NOT NULL", orderBy);
        Assert.DoesNotContain("COALESCE", orderBy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"CreatedAt\" DESC", orderBy);
    }

    /// <summary>
    /// Passing a null sort key is deliberate, and three of the facts above depend on it.
    /// PropertyFilterDto.SortBy is annotated non-nullable and carries a "CreatedAt" default,
    /// but the annotation is advisory at runtime: a request body with an explicit null writes
    /// one straight through it. That is precisely the case ApplySort's null-conditional call
    /// guards, so the suppression below asserts the hostile input rather than hiding it.
    /// </summary>
    private static string[] Sort(Property[] listings, string? sortBy, bool descending)
        => PropertyRepository
            .ApplySort(
                listings.AsQueryable(),
                new PropertyFilterDto { SortBy = sortBy!, SortDescending = descending },
                Now)
            .Select(property => property.Title)
            .ToArray();

    private static Property Listing(
        string title,
        int createdDaysAgo,
        DateTime? featuredFrom = null,
        decimal? price = null)
    {
        var listing = Property.Create(
            title,
            $"{title} description",
            Guid.NewGuid(),
            ListingType.ForSale);

        listing.CreatedAt = Now.AddDays(-createdDaysAgo);

        // One value drives all three explicit sort keys, so the Theory above can share a fixture.
        listing.PurchasePrice = price;
        listing.ColdRent = price;
        listing.Area = price;

        if (featuredFrom is { } from)
        {
            listing.MarkFeatured(TimeSpan.FromDays(30), from);
        }

        return listing;
    }
}
