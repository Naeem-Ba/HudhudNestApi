using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Application.Reviews.Commands.RateAgency;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Reviews.Queries.GetAgencyRatingEligibility;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Reviews.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Agencies;

/// <summary>
/// The agency's own rating: independent of any member's UserRating, only for people who really
/// dealt with the agency, never by its owner or members, one editable rating per rater.
/// </summary>
public sealed class AgencyRatingTests
{
    private static Agency NewAgency(Guid? ownerId = null) =>
        Agency.Create("XYZ Real Estate", "xyz", ownerId ?? Guid.NewGuid(), "SY", DateTime.UtcNow);

    private sealed class Rig
    {
        public Agency Agency { get; }
        public UserAccount Rater { get; }
        public Mock<IAgencyRatingRepository> Ratings { get; } = new();
        public Mock<IAgencyRepository> Agencies { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public List<AgencyRating> Added { get; } = [];

        public Rig(bool interaction = true, bool agencyActive = true)
        {
            Agency = NewAgency();
            if (!agencyActive)
                Agency.Deactivate(DateTime.UtcNow);
            Rater = UserAccount.Create(Guid.NewGuid(), "Layla", "K", DateTime.UtcNow);

            Agencies.Setup(x => x.GetByIdAsync(Agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Agency);
            Agencies.Setup(x => x.GetUserAccountAsync(Rater.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Rater);
            Ratings.Setup(x => x.HasInteractionWithAgencyAsync(Rater.Id, Agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(interaction);
            Ratings.Setup(x => x.AddAsync(It.IsAny<AgencyRating>(), It.IsAny<CancellationToken>()))
                .Callback<AgencyRating, CancellationToken>((r, _) => Added.Add(r)).Returns(Task.CompletedTask);
            Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public RateAgencyCommandHandler Handler() => new(Ratings.Object, Agencies.Object, Uow.Object);

        public RateAgencyCommand Command(Guid? raterId = null, int service = 5) =>
            new(Agency.Id, raterId ?? Rater.Id, service, 4, 5, 4, "good");
    }

    [Fact(DisplayName = "Rating an agency after a real interaction creates one rating by the caller")]
    public async Task Rate_AfterInteraction_Creates()
    {
        var rig = new Rig();

        var dto = await rig.Handler().Handle(rig.Command(), CancellationToken.None);

        var added = Assert.Single(rig.Added);
        Assert.Equal(rig.Agency.Id, added.AgencyId);
        Assert.Equal(rig.Rater.Id, added.RaterId);
        Assert.Equal(4.5, dto.OverallScore);
        rig.Uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "A repeat rating edits the existing one instead of adding another")]
    public async Task Rate_Twice_UpdatesInPlace()
    {
        var rig = new Rig();
        var existing = AgencyRating.Create(rig.Agency.Id, rig.Rater.Id, 1, 1, 1, 1);
        rig.Ratings.Setup(x => x.GetByRaterAndAgencyAsync(rig.Agency.Id, rig.Rater.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await rig.Handler().Handle(rig.Command(service: 5), CancellationToken.None);

        Assert.Empty(rig.Added);
        Assert.Equal(5, existing.Service);
    }

    [Fact(DisplayName = "The agency owner cannot rate their own agency")]
    public async Task Owner_CannotRate()
    {
        var rig = new Rig();
        rig.Agencies.Setup(x => x.GetUserAccountAsync(rig.Agency.OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserAccount.Create(rig.Agency.OwnerUserId, "O", "W", DateTime.UtcNow));

        await Assert.ThrowsAsync<DomainException>(() =>
            rig.Handler().Handle(rig.Command(raterId: rig.Agency.OwnerUserId), CancellationToken.None));
        Assert.Empty(rig.Added);
    }

    [Fact(DisplayName = "A member of the agency cannot rate it")]
    public async Task Member_CannotRate()
    {
        var rig = new Rig();
        rig.Rater.JoinAgency(rig.Agency.Id, DateTime.UtcNow);

        await Assert.ThrowsAsync<DomainException>(() => rig.Handler().Handle(rig.Command(), CancellationToken.None));
        Assert.Empty(rig.Added);
    }

    [Fact(DisplayName = "No interaction with the agency, no rating")]
    public async Task NoInteraction_Blocked()
    {
        var rig = new Rig(interaction: false);

        await Assert.ThrowsAsync<DomainException>(() => rig.Handler().Handle(rig.Command(), CancellationToken.None));
        Assert.Empty(rig.Added);
    }

    [Fact(DisplayName = "A deactivated agency cannot be rated (404, like its public page)")]
    public async Task InactiveAgency_NotFound()
    {
        var rig = new Rig(agencyActive: false);

        await Assert.ThrowsAsync<NotFoundException>(() => rig.Handler().Handle(rig.Command(), CancellationToken.None));
    }

    [Theory(DisplayName = "Scores outside 1..5 are rejected by the domain")]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task OutOfRangeScore_Rejected(int score)
    {
        var rig = new Rig();

        await Assert.ThrowsAsync<DomainException>(() =>
            rig.Handler().Handle(rig.Command(service: score), CancellationToken.None));
        Assert.Empty(rig.Added);
    }

    [Fact(DisplayName = "Eligibility: members/owner are told no, others depend on a real interaction, and an earlier rating is returned")]
    public async Task Eligibility_Reports_Each_Case()
    {
        var rig = new Rig();
        var handler = new GetAgencyRatingEligibilityQueryHandler(rig.Ratings.Object, rig.Agencies.Object);

        var customer = await handler.Handle(new GetAgencyRatingEligibilityQuery(rig.Agency.Id, rig.Rater.Id), CancellationToken.None);
        Assert.True(customer.CanRate);
        Assert.False(customer.AlreadyRated);

        var owner = await handler.Handle(new GetAgencyRatingEligibilityQuery(rig.Agency.Id, rig.Agency.OwnerUserId), CancellationToken.None);
        Assert.False(owner.CanRate);
        Assert.True(owner.IsMemberOrOwner);

        var stranger = new Rig(interaction: false);
        var strangerResult = await new GetAgencyRatingEligibilityQueryHandler(stranger.Ratings.Object, stranger.Agencies.Object)
            .Handle(new GetAgencyRatingEligibilityQuery(stranger.Agency.Id, stranger.Rater.Id), CancellationToken.None);
        Assert.False(strangerResult.CanRate);
        Assert.False(strangerResult.HasInteraction);
    }

    // ---- agency card on the listing contract -------------------------------------------------

    [Fact(DisplayName = "Listing card: active agency fills name, slug, logo and rating in ONE batch lookup; individual and inactive stay empty")]
    public async Task Enricher_FillsOnlyActiveAgencies_InOneBatch()
    {
        var active = NewAgency();
        var inactive = NewAgency();
        inactive.Deactivate(DateTime.UtcNow);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([active, inactive]);
        var ratings = new Mock<IAgencyRatingRepository>();
        ratings.Setup(x => x.GetOverallSummariesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, (double, int)> { [active.Id] = (4.26, 3) });

        var a1 = new PropertyDto { AgencyId = active.Id };
        var a2 = new PropertyDto { AgencyId = active.Id };
        var b = new PropertyDto { AgencyId = inactive.Id };
        var individual = new PropertyDto { AgencyId = null };

        await AgencyListingInfoEnricher.EnrichAsync([a1, a2, b, individual], agencies.Object, ratings.Object, CancellationToken.None);

        Assert.Equal("XYZ Real Estate", a1.AgencyName);
        Assert.Equal("xyz", a2.AgencySlug);
        Assert.Equal(4.3, a1.AgencyRatingAverage);
        Assert.Equal(3, a1.AgencyRatingCount);
        Assert.Null(b.AgencyName);
        Assert.Null(individual.AgencyName);
        Assert.Equal(0, individual.AgencyRatingCount);
        agencies.Verify(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Listing card: a page without any agency listing does no lookup at all")]
    public async Task Enricher_NoAgencyListings_NoQueries()
    {
        var agencies = new Mock<IAgencyRepository>();
        var ratings = new Mock<IAgencyRatingRepository>();

        await AgencyListingInfoEnricher.EnrichAsync([new PropertyDto()], agencies.Object, ratings.Object, CancellationToken.None);

        agencies.VerifyNoOtherCalls();
        ratings.VerifyNoOtherCalls();
    }
}
