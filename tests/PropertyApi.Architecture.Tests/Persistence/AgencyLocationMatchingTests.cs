using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Infrastructure.Repositories;

namespace PropertyApi.Architecture.Tests.Persistence;

/// <summary>
/// Valuation Stage 4 (Office Matching) — the actual DB-side matching rule
/// (AgencyRepository.ApplyActiveLocationFilter): active-only, exactly one location scope per
/// call, and never-select-twice exclusion. Lives here rather than in
/// PropertyApi.Application.Tests for the same reason ComparableListingsMatchingTests
/// (Stage 3) does: PropertyApi.Application.Tests deliberately does not reference
/// PropertyApi.Infrastructure. Exercised over LINQ-to-Objects, no database involved —
/// OfficeMatchingServiceTests (Application.Tests) separately covers the Service's own
/// level-by-level decision logic against a Moq'd IAgencyRepository.
/// </summary>
public sealed class AgencyLocationMatchingTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeighborhoodScope_MatchesExactNeighborhoodOnly()
    {
        var inScope = BuildAgency(governorateId: 1, districtId: 10, neighborhoodId: 100);
        var sameDistrictOtherNeighborhood = BuildAgency(governorateId: 1, districtId: 10, neighborhoodId: 101);

        var results = Filter([inScope, sameDistrictOtherNeighborhood], neighborhoodId: 100);

        Assert.Equal([inScope], results);
    }

    [Fact]
    public void DistrictScope_MatchesExactDistrictOnly()
    {
        var inScope = BuildAgency(governorateId: 1, districtId: 10, neighborhoodId: 999);
        var otherDistrict = BuildAgency(governorateId: 1, districtId: 20, neighborhoodId: 999);

        var results = Filter([inScope, otherDistrict], districtId: 10);

        Assert.Equal([inScope], results);
    }

    [Fact]
    public void GovernorateScope_MatchesAnyOfTheGivenGovernorateIds()
    {
        // Used for both Level 3 (a single governorate id) and Level 4 (several neighboring
        // governorate ids) — same method, just a wider set.
        var ownGovernorate = BuildAgency(governorateId: 1, districtId: 55, neighborhoodId: 555);
        var neighboringGovernorate = BuildAgency(governorateId: 2, districtId: 55, neighborhoodId: 555);
        var unrelatedGovernorate = BuildAgency(governorateId: 3, districtId: 55, neighborhoodId: 555);

        var results = Filter([ownGovernorate, neighboringGovernorate, unrelatedGovernorate], governorateIds: [1, 2]);

        Assert.Equal([ownGovernorate, neighboringGovernorate], results);
    }

    [Fact]
    public void Excludes_AgencyWithNoGovernorateId_FromGovernorateScope()
    {
        var unclassified = BuildAgency(governorateId: null, districtId: null, neighborhoodId: null);

        var results = Filter([unclassified], governorateIds: [1]);

        Assert.Empty(results);
    }

    [Fact]
    public void Excludes_InactiveAgency_RegardlessOfScope()
    {
        var inactive = BuildAgency(governorateId: 1, districtId: 10, neighborhoodId: 100, isActive: false);

        var results = Filter([inactive], neighborhoodId: 100);

        Assert.Empty(results);
    }

    [Fact]
    public void Excludes_AgencyIdsAlreadyInTheExcludeList()
    {
        var already = BuildAgency(governorateId: 1, districtId: 10, neighborhoodId: 100);
        var fresh = BuildAgency(governorateId: 1, districtId: 10, neighborhoodId: 100);

        var results = Filter([already, fresh], neighborhoodId: 100, excludeAgencyIds: [already.Id]);

        Assert.Equal([fresh], results);
    }

    [Fact]
    public void NoScopeGiven_ReturnsNoResults_NeverEveryActiveAgency()
    {
        var agency = BuildAgency(governorateId: 1, districtId: 10, neighborhoodId: 100);

        var results = Filter([agency], neighborhoodId: null, districtId: null, governorateIds: null);

        Assert.Empty(results);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static List<Agency> Filter(
        IEnumerable<Agency> agencies,
        int? neighborhoodId = null,
        int? districtId = null,
        IReadOnlyCollection<int>? governorateIds = null,
        IReadOnlyCollection<Guid>? excludeAgencyIds = null)
    {
        return AgencyRepository.ApplyActiveLocationFilter(
            agencies.AsQueryable(),
            neighborhoodId,
            districtId,
            governorateIds,
            excludeAgencyIds ?? Array.Empty<Guid>()).ToList();
    }

    private static Agency BuildAgency(
        int? governorateId,
        int? districtId,
        int? neighborhoodId,
        bool isActive = true)
    {
        var agency = Agency.Create(
            name: "Fixture Agency",
            slug: $"fixture-{Guid.NewGuid():N}",
            ownerUserId: Guid.NewGuid(),
            countryCode: "SY",
            utcNow: Now);

        agency.GovernorateId = governorateId;
        agency.DistrictId = districtId;
        agency.NeighborhoodId = neighborhoodId;

        if (!isActive)
            agency.Deactivate(Now);

        return agency;
    }
}
