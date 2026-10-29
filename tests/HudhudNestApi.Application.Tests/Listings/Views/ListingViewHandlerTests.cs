using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.Views;
using HudhudNestApi.Domain.Listings.Enums;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings.Views;

public sealed class ListingViewHandlerTests
{
    private static readonly Guid ListingId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AgencyId = Guid.NewGuid();

    private readonly Mock<IListingViewRepository> _repo = new();
    private readonly Mock<IListingViewPolicy> _policy = new();

    public ListingViewHandlerTests()
    {
        _policy.Setup(p => p.HashViewer(It.IsAny<string>())).Returns<string>(s => "h:" + s);
        _policy.Setup(p => p.BucketFor(It.IsAny<DateTime>())).Returns(42);
        _policy.SetupGet(p => p.RetentionDays).Returns(90);
        Target(visible: true);
        _repo.Setup(r => r.GetCountsAsync(It.IsAny<ListingKind>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long> { [ListingId] = 7 });
        _repo.Setup(r => r.TryRecordAsync(It.IsAny<ListingKind>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, 8L));
    }

    private void Target(bool visible, Guid? agency = null)
        => _repo.Setup(r => r.GetTargetAsync(It.IsAny<ListingKind>(), ListingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListingViewTarget(OwnerId, agency, "title", visible));

    private Task<ListingViewResultDto> Record(
        Guid? userId = null, bool admin = false, string? token = "0123456789abcdef0123",
        bool bot = false, bool test = false)
        => new RecordListingViewCommandHandler(_repo.Object, _policy.Object).Handle(
            new RecordListingViewCommand(ListingKind.Property, ListingId, userId, admin, token, "fp", bot, test), default);

    private void VerifyNeverRecorded()
        => _repo.Verify(r => r.TryRecordAsync(It.IsAny<ListingKind>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task AnonymousVisitor_IsCounted_AndTotalComesFromTheServer()
    {
        var result = await Record();
        Assert.True(result.Counted);
        Assert.Equal(8, result.ViewCount);
        Assert.Null(result.Reason);
        _repo.Verify(r => r.TryRecordAsync(ListingKind.Property, ListingId, "h:v:0123456789abcdef0123", 42, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepeatInsideTheCooldown_IsNotCounted_AndReportsCooldown()
    {
        _repo.Setup(r => r.TryRecordAsync(It.IsAny<ListingKind>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, 8L));
        var result = await Record();
        Assert.False(result.Counted);
        Assert.Equal("cooldown", result.Reason);
        Assert.Equal(8, result.ViewCount);
    }

    [Fact]
    public async Task SignedInUser_IsIdentifiedByAccount_NotByTheVisitorToken()
    {
        var user = Guid.NewGuid();
        await Record(userId: user, token: "0123456789abcdef0123");
        _repo.Verify(r => r.TryRecordAsync(ListingKind.Property, ListingId, $"h:u:{user:N}", 42, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in it 0123456789")]
    [InlineData("<script>alert(1)</script>0123456789")]
    public async Task MissingOrMalformedVisitorToken_FallsBackToServerFingerprint(string? token)
    {
        await Record(token: token);
        _repo.Verify(r => r.TryRecordAsync(ListingKind.Property, ListingId, "h:f:fp", 42, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownListing_IsNotFound()
    {
        _repo.Setup(r => r.GetTargetAsync(It.IsAny<ListingKind>(), ListingId, It.IsAny<CancellationToken>())).ReturnsAsync((ListingViewTarget?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => Record());
        VerifyNeverRecorded();
    }

    [Fact]
    public async Task UnpublishedOrHiddenListing_IsNotFound_EvenForBotsAndOwners()
    {
        Target(visible: false);
        await Assert.ThrowsAsync<NotFoundException>(() => Record());
        await Assert.ThrowsAsync<NotFoundException>(() => Record(bot: true));
        await Assert.ThrowsAsync<NotFoundException>(() => Record(userId: OwnerId));
        VerifyNeverRecorded();
    }

    [Theory]
    [InlineData(true, false, "automated")]
    [InlineData(false, true, "test-traffic")]
    public async Task BotsAndTestTraffic_AreNeverCounted(bool bot, bool test, string reason)
    {
        var result = await Record(bot: bot, test: test);
        Assert.False(result.Counted);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(7, result.ViewCount);
        VerifyNeverRecorded();
    }

    [Fact]
    public async Task Owner_AndAdmin_AreNotAnAudience()
    {
        Assert.Equal("own-listing", (await Record(userId: OwnerId)).Reason);
        Assert.Equal("own-listing", (await Record(userId: Guid.NewGuid(), admin: true)).Reason);
        VerifyNeverRecorded();
    }

    [Fact]
    public async Task AgencyMember_OfTheListingsAgency_IsNotCounted_ButAnOutsiderIs()
    {
        Target(visible: true, agency: AgencyId);
        var member = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        _repo.Setup(r => r.GetUserAgencyIdAsync(member, It.IsAny<CancellationToken>())).ReturnsAsync(AgencyId);
        _repo.Setup(r => r.GetUserAgencyIdAsync(outsider, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        Assert.Equal("own-listing", (await Record(userId: member)).Reason);
        Assert.True((await Record(userId: outsider)).Counted);
    }

    [Theory]
    [InlineData("0123456789abcdef", true)]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef", true)]
    [InlineData("0123456789abcde", false)]
    [InlineData("0123456789abcdef_", false)]
    [InlineData(null, false)]
    public void VisitorTokenFormat(string? token, bool valid)
        => Assert.Equal(valid, RecordListingViewCommandHandler.IsValidVisitorToken(token));

    // ── statistics authorization ─────────────────────────────────────────

    [Fact]
    public async Task AgencyScopeStats_AreForbidden_ForAnyoneButAnAgencyOwner()
    {
        var handler = new GetListingViewStatsQueryHandler(_repo.Object, _policy.Object);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetListingViewStatsQuery(Guid.NewGuid(), IsAgencyOwner: false, "agency"), default));
        _repo.Verify(r => r.GetAgencyStatsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OwnStats_AreScopedToTheCaller_AndSortedByRecentViews()
    {
        var user = Guid.NewGuid();
        _repo.Setup(r => r.GetOwnerStatsAsync(user, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new ListingViewStatsRow(ListingKind.Property, Guid.NewGuid(), "a", 10, 1, 2, 2),
            new ListingViewStatsRow(ListingKind.ShortStay, Guid.NewGuid(), "b", 30, 5, 9, 4),
        });
        var result = await new GetListingViewStatsQueryHandler(_repo.Object, _policy.Object)
            .Handle(new GetListingViewStatsQuery(user, false, "mine"), default);

        Assert.Equal("mine", result.Scope);
        Assert.Equal(new[] { "b", "a" }, result.Items.Select(i => i.Title));
        Assert.Equal(40, result.TotalViews);
        Assert.Equal(11, result.Views30Days);
        Assert.Equal(90, result.HistoryRetentionDays);
    }

    [Fact]
    public async Task Trend_IsForbidden_ForSomeoneWhoDoesNotOwnTheListing()
    {
        var handler = new GetListingViewTrendQueryHandler(_repo.Object, _policy.Object);
        var stranger = Guid.NewGuid();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetListingViewTrendQuery(ListingKind.Property, ListingId, stranger, false, false, 30), default));

        // An agency owner of ANOTHER agency is a stranger too.
        Target(visible: true, agency: AgencyId);
        _repo.Setup(r => r.GetUserAgencyIdAsync(stranger, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetListingViewTrendQuery(ListingKind.Property, ListingId, stranger, false, true, 30), default));
    }

    [Fact]
    public async Task Trend_FillsMissingDaysWithZero_AndClampsTheWindow()
    {
        _repo.Setup(r => r.GetDailyViewsAsync(ListingKind.Property, ListingId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DateOnly, int> { [DateOnly.FromDateTime(DateTime.UtcNow)] = 3 });
        var handler = new GetListingViewTrendQueryHandler(_repo.Object, _policy.Object);

        var result = await handler.Handle(new GetListingViewTrendQuery(ListingKind.Property, ListingId, OwnerId, false, false, 7), default);
        Assert.Equal(7, result.Points.Count);
        Assert.Equal(3, result.Points[^1].Views);
        Assert.All(result.Points.Take(6), p => Assert.Equal(0, p.Views));
        Assert.Equal(7, result.TotalViews);

        var clamped = await handler.Handle(new GetListingViewTrendQuery(ListingKind.Property, ListingId, OwnerId, false, false, 9999), default);
        Assert.Equal(90, clamped.Days);
    }
}
