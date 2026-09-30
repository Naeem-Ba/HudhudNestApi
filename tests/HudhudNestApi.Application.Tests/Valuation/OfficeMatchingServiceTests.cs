using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Application.Valuation.Services;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Tests.Valuation;

/// <summary>
/// Stage 4 — OfficeMatchingService's own decision logic (which levels get tried, in what
/// order, when it stops, how duplicates are prevented). Uses Moq for IAgencyRepository, the
/// same convention AgencyInvitationTests/AgencyTests already use for this module — unlike
/// IPropertyRepository's hand-written stubs elsewhere in this codebase.
///
/// The real DB-side WHERE-clause matching rule (AgencyRepository.ApplyActiveLocationFilter)
/// is separately covered, LINQ-to-Objects, in
/// HudhudNestApi.Architecture.Tests/Persistence/AgencyLocationMatchingTests.cs — same split as
/// Stage 3's GetComparableListingsQueryHandlerTests / ComparableListingsMatchingTests.
/// </summary>
public sealed class OfficeMatchingServiceTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid InquiryId = Guid.NewGuid();

    // ── Test 1 — 3+ offices at Neighborhood level ──────────────────

    [Fact]
    public async Task Handle_ThreeOfficesInNeighborhood_ReturnsExactlyThree_AtNeighborhoodLevel()
    {
        var (service, agencies, _) = Build();

        SetupLevel(agencies, neighborhoodId: 100, agencyIds: [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: 10, neighborhoodId: 100, Now);

        // 4 were available in the neighborhood; only 3 are taken.
        Assert.Equal(3, result.Count);
        Assert.All(result.SelectedOffices, o => Assert.Equal(ValuationMatchLevel.Neighborhood, o.MatchLevel));
        Assert.True(result.HasMinimumCoverage);
        Assert.False(result.InsufficientOfficeCoverage);
        Assert.True(result.MatchingCompleted);

        // District/Governorate must never even be queried once Neighborhood alone reached 3.
        agencies.Verify(x => x.FindActiveByLocationAsync(null, 10, null, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        agencies.Verify(x => x.FindActiveByLocationAsync(null, null, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 2 — 1 in Neighborhood, escalates straight to Governorate ──

    [Fact]
    public async Task Handle_OneOfficeInNeighborhood_EscalatesToGovernorate_FillingUpToThree()
    {
        var (service, agencies, _) = Build();

        var neighborhoodOffice = Guid.NewGuid();
        var governorateOffices = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        SetupLevel(agencies, neighborhoodId: 100, agencyIds: [neighborhoodOffice]);
        SetupLevel(agencies, districtId: 10, agencyIds: []); // District has none new.
        SetupLevel(agencies, governorateIds: [1], agencyIds: governorateOffices);

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: 10, neighborhoodId: 100, Now);

        Assert.Equal(3, result.Count);
        Assert.Equal(neighborhoodOffice, result.SelectedOffices[0].AgencyId);
        Assert.Equal(ValuationMatchLevel.Neighborhood, result.SelectedOffices[0].MatchLevel);
        Assert.Equal(ValuationMatchLevel.Governorate, result.SelectedOffices[1].MatchLevel);
        Assert.Equal(ValuationMatchLevel.Governorate, result.SelectedOffices[2].MatchLevel);

        // No duplicate agency ids across levels.
        Assert.Equal(result.SelectedOffices.Select(o => o.AgencyId).Distinct().Count(), result.Count);
        Assert.True(result.HasMinimumCoverage);
    }

    // ── Test 3 — Governorate short, falls back to neighboring governorates ──

    [Fact]
    public async Task Handle_GovernorateHasFewerThanThree_FallsBackToNeighboringGovernorates()
    {
        var (service, agencies, neighbors) = Build();

        SetupLevel(agencies, neighborhoodId: 100, agencyIds: []);
        SetupLevel(agencies, districtId: 10, agencyIds: []);
        SetupLevel(agencies, governorateIds: [1], agencyIds: [Guid.NewGuid()]); // only 1 in own governorate

        neighbors.Setup(x => x.GetNeighborIdsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([2, 3]);

        var neighboringOffices = new[] { Guid.NewGuid(), Guid.NewGuid() };
        SetupLevel(agencies, governorateIds: [2, 3], agencyIds: neighboringOffices);

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: 10, neighborhoodId: 100, Now);

        Assert.Equal(3, result.Count);
        Assert.Equal(ValuationMatchLevel.Governorate, result.SelectedOffices[0].MatchLevel);
        Assert.Equal(ValuationMatchLevel.GovernorateNeighboring, result.SelectedOffices[1].MatchLevel);
        Assert.Equal(ValuationMatchLevel.GovernorateNeighboring, result.SelectedOffices[2].MatchLevel);
        Assert.True(result.HasMinimumCoverage);
    }

    // ── Test 4 — still short even after neighboring governorates ──

    [Fact]
    public async Task Handle_StillFewerThanThree_AfterNeighboringGovernorates_ReportsInsufficientCoverage_NotPending()
    {
        var (service, agencies, neighbors) = Build();

        SetupLevel(agencies, neighborhoodId: 100, agencyIds: []);
        SetupLevel(agencies, districtId: 10, agencyIds: []);
        var onlyOffice = Guid.NewGuid();
        SetupLevel(agencies, governorateIds: [1], agencyIds: [onlyOffice]);

        neighbors.Setup(x => x.GetNeighborIdsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([2]);
        SetupLevel(agencies, governorateIds: [2], agencyIds: []);

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: 10, neighborhoodId: 100, Now);

        Assert.Equal(1, result.Count);
        Assert.False(result.HasMinimumCoverage);
        Assert.True(result.InsufficientOfficeCoverage);
        // Never left "pending" — matching always concludes definitively in one pass.
        Assert.True(result.MatchingCompleted);
    }

    [Fact]
    public async Task Handle_NoOfficesAnywhere_ReturnsZero_WithInsufficientCoverage()
    {
        var (service, agencies, neighbors) = Build();

        SetupLevel(agencies, neighborhoodId: 100, agencyIds: []);
        SetupLevel(agencies, districtId: 10, agencyIds: []);
        SetupLevel(agencies, governorateIds: [1], agencyIds: []);
        neighbors.Setup(x => x.GetNeighborIdsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<int>());

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: 10, neighborhoodId: 100, Now);

        Assert.Equal(0, result.Count);
        Assert.False(result.HasMinimumCoverage);
        Assert.True(result.InsufficientOfficeCoverage);
        Assert.True(result.MatchingCompleted);

        // No adjacency data at all -> Level 4's repository query must never even run.
        agencies.Verify(x => x.FindActiveByLocationAsync(null, null, It.Is<IReadOnlyCollection<int>>(g => g.Contains(2) || g.Contains(3)), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InquiryWithNoNeighborhoodOrDistrict_SkipsStraightToGovernorate()
    {
        var (service, agencies, _) = Build();

        var offices = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        SetupLevel(agencies, governorateIds: [1], agencyIds: offices);

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: null, neighborhoodId: null, Now);

        Assert.Equal(3, result.Count);
        Assert.All(result.SelectedOffices, o => Assert.Equal(ValuationMatchLevel.Governorate, o.MatchLevel));

        agencies.Verify(x => x.FindActiveByLocationAsync(It.IsAny<int?>(), null, null, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        agencies.Verify(x => x.FindActiveByLocationAsync(null, It.IsAny<int?>(), null, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameAgencyReturnedAtTwoLevels_IsSelectedOnlyOnce()
    {
        // Defensive Application-logic duplicate guard: even if the repository stub (unlike
        // the real one) returned an agency id that was already selected, the service itself
        // must not add it twice.
        var (service, agencies, neighbors) = Build();

        var duplicate = Guid.NewGuid();
        var freshOne = Guid.NewGuid();

        SetupLevel(agencies, neighborhoodId: 100, agencyIds: [duplicate]);
        SetupLevel(agencies, districtId: 10, agencyIds: [duplicate, freshOne]); // stub misbehaves on purpose
        SetupLevel(agencies, governorateIds: [1], agencyIds: []); // still short of 3 -> level 3 does run
        neighbors.Setup(x => x.GetNeighborIdsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<int>());

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: 10, neighborhoodId: 100, Now);

        Assert.Equal(2, result.Count);
        Assert.Equal([duplicate, freshOne], result.SelectedOffices.Select(o => o.AgencyId));
    }

    [Fact]
    public async Task Handle_BuildsAnInMemoryInvitation_ForEachSelectedOffice_WithCorrectMatchLevel()
    {
        var (service, agencies, neighbors) = Build();
        var agencyId = Guid.NewGuid();
        SetupLevel(agencies, neighborhoodId: 100, agencyIds: [agencyId]);
        SetupLevel(agencies, districtId: 10, agencyIds: []);
        SetupLevel(agencies, governorateIds: [1], agencyIds: []);
        neighbors.Setup(x => x.GetNeighborIdsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<int>());

        var result = await service.MatchOfficesAsync(InquiryId, governorateId: 1, districtId: 10, neighborhoodId: 100, Now);

        var match = Assert.Single(result.SelectedOffices);
        Assert.Equal(agencyId, match.Invitation.AgencyId);
        Assert.Equal(InquiryId, match.Invitation.InquiryId);
        Assert.Equal(ValuationMatchLevel.Neighborhood, match.Invitation.MatchLevel);
        Assert.Equal(Now, match.Invitation.SentAt);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static (OfficeMatchingService Service, Mock<IAgencyRepository> Agencies, Mock<IGovernorateNeighborProvider> Neighbors) Build()
    {
        var agencies = new Mock<IAgencyRepository>(MockBehavior.Strict);
        var neighbors = new Mock<IGovernorateNeighborProvider>(MockBehavior.Strict);
        var service = new OfficeMatchingService(agencies.Object, neighbors.Object, NullLogger<OfficeMatchingService>.Instance);
        return (service, agencies, neighbors);
    }

    private static void SetupLevel(
        Mock<IAgencyRepository> agencies,
        Guid[] agencyIds,
        int? neighborhoodId = null,
        int? districtId = null,
        int[]? governorateIds = null)
    {
        // Deliberately NOT "governorateIds == null ? null : It.Is<...>(...)" — wrapping an
        // It.Is call inside a conditional expression defeats Moq's expression-tree matcher
        // recognition (It.Is must be a direct top-level argument expression), so it silently
        // stops matching anything non-null instead of throwing at setup time.
        agencies
            .Setup(x => x.FindActiveByLocationAsync(
                neighborhoodId,
                districtId,
                It.Is<IReadOnlyCollection<int>?>(g => GovernorateIdsMatch(g, governorateIds)),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(agencyIds.Select(BuildAgency).ToList());
    }

    private static bool GovernorateIdsMatch(IReadOnlyCollection<int>? actual, int[]? expected)
    {
        if (expected is null)
            return actual is null;

        return actual is not null && actual.SequenceEqual(expected);
    }

    private static Agency BuildAgency(Guid id)
    {
        // Id is BaseEntity's own Guid.NewGuid() field initializer (public get, protected
        // set — Agency.Create has no way to assign a caller-chosen id). Overwritten via
        // reflection purely so these tests can assert on a specific, caller-known id instead
        // of having to capture whatever random Guid Create() happened to generate.
        var agency = Agency.Create("Test Agency", $"test-{id:N}", Guid.NewGuid(), "SY", Now);
        typeof(Agency).GetProperty(nameof(Agency.Id))!.SetValue(agency, id);
        return agency;
    }
}
