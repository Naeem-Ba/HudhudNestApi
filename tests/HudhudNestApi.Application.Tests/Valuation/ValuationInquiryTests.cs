using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Domain.Valuation.Enums;
using Xunit;

namespace HudhudNestApi.Application.Tests.Valuation;

/// <summary>
/// Domain-only tests for ValuationInquiry (Phase 2 — Valuation Domain Layer). Deliberately
/// touches nothing but HudhudNestApi.Domain: no DbContext, no repository, no HTTP — see
/// "Domain independence" tests at the bottom, which is the point of this whole module.
/// </summary>
public sealed class ValuationInquiryTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── Location hierarchy ───────────────────────────────────────

    [Fact]
    public void Create_WithGovernorateOnly_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow);

        Assert.Equal(1, inquiry.GovernorateId);
        Assert.Null(inquiry.DistrictId);
        Assert.Null(inquiry.NeighborhoodId);
    }

    [Fact]
    public void Create_WithFullLocation_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow,
            districtId: 10,
            neighborhoodId: 100);

        Assert.Equal(1, inquiry.GovernorateId);
        Assert.Equal(10, inquiry.DistrictId);
        Assert.Equal(100, inquiry.NeighborhoodId);
    }

    [Fact]
    public void Create_WithoutGovernorate_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            governorateId: 0,
            requestType: ListingType.ForSale,
            utcNow: UtcNow));
    }

    [Fact]
    public void Create_WithNegativeGovernorateId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            governorateId: -1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow));
    }

    [Fact]
    public void Create_WithNeighborhoodButNoDistrict_Throws()
    {
        // Locally-checkable part of the hierarchy rule (section 10): a neighborhood cannot
        // stand without a district. Whether it belongs to the *right* district needs a
        // lookup and is explicitly out of scope for this Domain-only phase.
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow,
            neighborhoodId: 100));
    }

    [Fact]
    public void Create_WithInvalidDistrictId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow,
            districtId: 0));
    }

    [Fact]
    public void Create_WithInvalidNeighborhoodId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow,
            districtId: 10,
            neighborhoodId: -1));
    }

    // ── ExpiresAt ─────────────────────────────────────────────────

    [Fact]
    public void Create_SetsExpiresAt_ExactlyTwentyFourHoursAfterCreatedAt()
    {
        var inquiry = ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForRent,
            utcNow: UtcNow);

        Assert.Equal(UtcNow, inquiry.CreatedAt);
        Assert.Equal(UtcNow.AddHours(24), inquiry.ExpiresAt);
        Assert.Equal(TimeSpan.FromHours(24), inquiry.ExpiresAt - inquiry.CreatedAt);
    }

    // ── Requester: registered vs. anonymous ──────────────────────

    [Fact]
    public void Create_WithoutRequester_Succeeds_ForAnonymousVisitor()
    {
        var inquiry = ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow);

        Assert.Null(inquiry.RequesterId);
    }

    [Fact]
    public void Create_WithRequesterId_AttachesTheUser()
    {
        var requesterId = Guid.NewGuid();

        var inquiry = ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow,
            requesterId: requesterId);

        Assert.Equal(requesterId, inquiry.RequesterId);
    }

    [Fact]
    public void Create_WithEmptyRequesterId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            governorateId: 1,
            requestType: ListingType.ForSale,
            utcNow: UtcNow,
            requesterId: Guid.Empty));
    }

    // ── RequestType (reuses ListingType) ──────────────────────────

    [Fact]
    public void Create_ForSale_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        Assert.Equal(ListingType.ForSale, inquiry.RequestType);
    }

    [Fact]
    public void Create_ForRent_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForRent, UtcNow);
        Assert.Equal(ListingType.ForRent, inquiry.RequestType);
    }

    [Fact]
    public void Create_WithUndefinedRequestType_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            governorateId: 1,
            requestType: (ListingType)999,
            utcNow: UtcNow));
    }

    // ── Area / Rooms ("null or positive", mirroring Property's own DB constraints) ──

    [Fact]
    public void Create_WithoutAreaOrRooms_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        Assert.Null(inquiry.Area);
        Assert.Null(inquiry.Rooms);
    }

    [Fact]
    public void Create_WithPositiveAreaAndRooms_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(
            1, ListingType.ForSale, UtcNow, area: 120.5m, rooms: 3);

        Assert.Equal(120.5m, inquiry.Area);
        Assert.Equal(3, inquiry.Rooms);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_WithNonPositiveArea_Throws(decimal area)
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            1, ListingType.ForSale, UtcNow, area: area));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveRooms_Throws(int rooms)
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            1, ListingType.ForSale, UtcNow, rooms: rooms));
    }

    [Fact]
    public void Create_WithInvalidPropertyTypeId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationInquiry.Create(
            1, ListingType.ForSale, UtcNow, propertyTypeId: 0));
    }

    // ── Status state-machine ──────────────────────────────────────

    [Fact]
    public void Create_StartsAsPending()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        Assert.Equal(ValuationInquiryStatus.Pending, inquiry.Status);
    }

    [Fact]
    public void MarkMatchedFromListings_FromPending_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);

        inquiry.MarkMatchedFromListings(UtcNow.AddMinutes(5));

        Assert.Equal(ValuationInquiryStatus.MatchedFromListings, inquiry.Status);
    }

    [Fact]
    public void MarkMatchedFromListings_WhenNotPending_Throws()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        inquiry.MarkMatchedFromListings(UtcNow);

        Assert.Throws<DomainException>(() => inquiry.MarkMatchedFromListings(UtcNow));
    }

    [Fact]
    public void MarkAwaitingOfficeResponses_FromPending_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);

        inquiry.MarkAwaitingOfficeResponses(UtcNow);

        Assert.Equal(ValuationInquiryStatus.AwaitingOfficeResponses, inquiry.Status);
    }

    [Fact]
    public void MarkAwaitingOfficeResponses_FromMatchedFromListings_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        inquiry.MarkMatchedFromListings(UtcNow);

        inquiry.MarkAwaitingOfficeResponses(UtcNow);

        Assert.Equal(ValuationInquiryStatus.AwaitingOfficeResponses, inquiry.Status);
    }

    [Fact]
    public void MarkCompleted_FromPending_Throws()
    {
        // No matching work has happened yet — "completed" straight from Pending is not real.
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);

        Assert.Throws<DomainException>(() => inquiry.MarkCompleted(UtcNow));
    }

    [Fact]
    public void MarkCompleted_FromAwaitingOfficeResponses_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        inquiry.MarkAwaitingOfficeResponses(UtcNow);

        inquiry.MarkCompleted(UtcNow);

        Assert.Equal(ValuationInquiryStatus.Completed, inquiry.Status);
    }

    [Fact]
    public void Expire_FromPending_Succeeds()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);

        inquiry.Expire(inquiry.ExpiresAt);

        Assert.Equal(ValuationInquiryStatus.Expired, inquiry.Status);
    }

    [Fact]
    public void Expire_WhenAlreadyCompleted_Throws()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        inquiry.MarkAwaitingOfficeResponses(UtcNow);
        inquiry.MarkCompleted(UtcNow);

        Assert.Throws<DomainException>(() => inquiry.Expire(UtcNow));
    }

    [Fact]
    public void IsExpired_BeforeWindowElapses_IsFalse()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        Assert.False(inquiry.IsExpired(UtcNow.AddHours(23)));
    }

    [Fact]
    public void IsExpired_AfterWindowElapses_IsTrue()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        Assert.True(inquiry.IsExpired(UtcNow.AddHours(24)));
    }

    [Fact]
    public void IsExpired_OnceCompleted_IsFalse()
    {
        // A finished inquiry is not "expired" even past its window — Completed is terminal.
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        inquiry.MarkAwaitingOfficeResponses(UtcNow);
        inquiry.MarkCompleted(UtcNow);

        Assert.False(inquiry.IsExpired(UtcNow.AddDays(1)));
    }

    // ── Domain independence (section 32) ─────────────────────────

    [Fact]
    public void DomainModel_RequiresNoInfrastructure_ToConstructOrTransition()
    {
        // The whole point of this test: everything above ran with nothing but
        // HudhudNestApi.Domain — no DbContext, repository, HTTP client, or Angular in sight.
        // This fact itself is the assertion; if the module needed any of those to compile
        // or run, this test file wouldn't exist in this project at all.
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, UtcNow);
        inquiry.MarkMatchedFromListings(UtcNow);
        inquiry.MarkAwaitingOfficeResponses(UtcNow);
        inquiry.MarkCompleted(UtcNow);

        Assert.Equal(ValuationInquiryStatus.Completed, inquiry.Status);
    }
}
