using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Ratings;
using HudhudNestApi.Application.Agencies.Ratings.Commands.RateAgency;
using HudhudNestApi.Application.Agencies.Ratings.Queries.GetAgencyRatingEligibility;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Agencies;

/// <summary>
/// Agency (office) rating: the entity's invariants (scores 1..5, comment length, in-place edit),
/// the "owner and members cannot rate" rule, the interaction requirement, create-vs-edit, and the
/// eligibility flags the app page renders. The HTTP behaviour is covered by the integration tests.
/// </summary>
public sealed class AgencyRatingTests
{
    // ── Entity ───────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Create_AcceptsEveryScoreInRange(int score)
    {
        var rating = AgencyRating.Create(Guid.NewGuid(), Guid.NewGuid(), score, score, score, score);

        Assert.Equal(score, rating.Service);
        Assert.Equal(score, rating.OverallScore);
    }

    [Theory]
    [InlineData(0, 3, 3, 3)]
    [InlineData(3, 6, 3, 3)]
    [InlineData(3, 3, -1, 3)]
    [InlineData(3, 3, 3, 0)]
    public void Create_RejectsAnyScoreOutsideOneToFive(int s, int p, int r, int o)
        => Assert.Throws<DomainException>(() => AgencyRating.Create(Guid.NewGuid(), Guid.NewGuid(), s, p, r, o));

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<DomainException>(() => AgencyRating.Create(Guid.Empty, Guid.NewGuid(), 3, 3, 3, 3));
        Assert.Throws<DomainException>(() => AgencyRating.Create(Guid.NewGuid(), Guid.Empty, 3, 3, 3, 3));
    }

    [Fact]
    public void Comment_IsTrimmed_BlankBecomesNull_AndThousandCharactersIsTheLimit()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Equal("جيد", AgencyRating.Create(a, b, 4, 4, 4, 4, "  جيد  ").Comment);
        Assert.Null(AgencyRating.Create(a, b, 4, 4, 4, 4, "   ").Comment);
        Assert.Equal(1000, AgencyRating.Create(a, b, 4, 4, 4, 4, new string('x', 1000)).Comment!.Length);
        Assert.Throws<DomainException>(() => AgencyRating.Create(a, b, 4, 4, 4, 4, new string('x', 1001)));
    }

    [Fact]
    public void OverallScore_IsTheMeanOfTheFourCriteria()
    {
        var rating = AgencyRating.Create(Guid.NewGuid(), Guid.NewGuid(), 5, 4, 4, 3);

        Assert.Equal(4.0, rating.OverallScore, 3);
    }

    [Fact]
    public void Update_EditsInPlace_ValidatesAgain_AndStampsUpdatedAt()
    {
        var rating = AgencyRating.Create(Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1, 1, "سيئ");
        var id = rating.Id;
        var when = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        rating.Update(5, 5, 4, 4, "تحسّن", when);

        Assert.Equal(id, rating.Id);
        Assert.Equal(5, rating.Service);
        Assert.Equal("تحسّن", rating.Comment);
        Assert.Equal(when, rating.UpdatedAt);
        Assert.Throws<DomainException>(() => rating.Update(9, 5, 4, 4, null, when));
        Assert.Throws<DomainException>(() => rating.Update(5, 5, 4, 4, new string('x', 1001), when));
    }

    // ── Validator ────────────────────────────────────────────────

    [Fact]
    public void Validator_RejectsOutOfRangeScoresLongCommentAndNul_AcceptsValid()
    {
        var validator = new RateAgencyCommandValidator();
        var ok = new RateAgencyCommand(Guid.NewGuid(), Guid.NewGuid(), 5, 4, 3, 2, "تعليق");

        Assert.True(validator.Validate(ok).IsValid);
        Assert.False(validator.Validate(ok with { Service = 0 }).IsValid);
        Assert.False(validator.Validate(ok with { OverallExperience = 6 }).IsValid);
        Assert.False(validator.Validate(ok with { Comment = new string('x', 1001) }).IsValid);
        Assert.False(validator.Validate(ok with { Comment = "a\0b" }).IsValid);
        Assert.True(validator.Validate(ok with { Comment = null }).IsValid);
    }

    // ── Policy ───────────────────────────────────────────────────

    [Fact]
    public void Policy_OwnerAndMembersAreBlocked_OutsidersAreNot()
    {
        var owner = NewUser();
        var agency = NewAgency(owner.Id);
        var member = NewUser();
        member.JoinAgency(agency.Id, DateTime.UtcNow);
        var outsider = NewUser();

        Assert.True(AgencyRatingPolicy.IsMemberOrOwner(agency, owner));
        Assert.True(AgencyRatingPolicy.IsMemberOrOwner(agency, member));
        Assert.False(AgencyRatingPolicy.IsMemberOrOwner(agency, outsider));
    }

    // ── Handler ──────────────────────────────────────────────────

    [Fact]
    public async Task Rate_UnknownOrInactiveOffice_IsNotFound()
    {
        var rater = NewUser();
        var inactive = NewAgency(Guid.NewGuid());
        inactive.Deactivate(DateTime.UtcNow);
        var fixture = new Fixture(inactive, rater);

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Handler.Handle(Command(inactive.Id, rater.Id), default));
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Handler.Handle(Command(Guid.NewGuid(), rater.Id), default));
    }

    [Fact]
    public async Task Rate_OwnerOrMember_IsForbidden_EvenWithInteraction()
    {
        var owner = NewUser();
        var agency = NewAgency(owner.Id);
        var member = NewUser();
        member.JoinAgency(agency.Id, DateTime.UtcNow);
        var fixture = new Fixture(agency, owner, member) { HasInteraction = true };

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Handler.Handle(Command(agency.Id, owner.Id), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Handler.Handle(Command(agency.Id, member.Id), default));
        fixture.Ratings.Verify(r => r.AddAsync(It.IsAny<AgencyRating>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rate_WithoutInteraction_IsForbidden()
    {
        var rater = NewUser();
        var agency = NewAgency(Guid.NewGuid());
        var fixture = new Fixture(agency, rater) { HasInteraction = false };

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Handler.Handle(Command(agency.Id, rater.Id), default));
        fixture.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rate_FirstTimeCreates_SecondTimeEditsTheSameRow()
    {
        var rater = NewUser();
        var agency = NewAgency(Guid.NewGuid());
        var fixture = new Fixture(agency, rater) { HasInteraction = true };

        var first = await fixture.Handler.Handle(Command(agency.Id, rater.Id) with { Service = 2 }, default);
        Assert.True(first.Created);
        Assert.Equal(2, first.Rating.Service);

        var second = await fixture.Handler.Handle(Command(agency.Id, rater.Id) with { Service = 5, Comment = "أفضل" }, default);
        Assert.False(second.Created);
        Assert.Equal(first.Rating.Id, second.Rating.Id);
        Assert.Equal(5, second.Rating.Service);
        Assert.Equal("أفضل", second.Rating.Comment);
        fixture.Ratings.Verify(r => r.AddAsync(It.IsAny<AgencyRating>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Eligibility ──────────────────────────────────────────────

    [Fact]
    public async Task Eligibility_ReflectsMembershipInteractionAndExistingRating()
    {
        var owner = NewUser();
        var agency = NewAgency(owner.Id);
        var outsider = NewUser();
        var fixture = new Fixture(agency, owner, outsider);

        var query = new GetAgencyRatingEligibilityQueryHandler(
            fixture.Agencies.Object, fixture.Ratings.Object, fixture.Accounts.Object);

        var ownerResult = await query.Handle(new GetAgencyRatingEligibilityQuery(agency.Id, owner.Id), default);
        Assert.False(ownerResult.CanRate);
        Assert.True(ownerResult.IsMemberOrOwner);

        fixture.HasInteraction = false;
        var noInteraction = await query.Handle(new GetAgencyRatingEligibilityQuery(agency.Id, outsider.Id), default);
        Assert.False(noInteraction.CanRate);
        Assert.False(noInteraction.HasInteraction);
        Assert.False(noInteraction.AlreadyRated);

        fixture.HasInteraction = true;
        var eligible = await query.Handle(new GetAgencyRatingEligibilityQuery(agency.Id, outsider.Id), default);
        Assert.True(eligible.CanRate);
        Assert.Null(eligible.ExistingRating);

        await fixture.Handler.Handle(Command(agency.Id, outsider.Id), default);
        var after = await query.Handle(new GetAgencyRatingEligibilityQuery(agency.Id, outsider.Id), default);
        Assert.True(after.AlreadyRated);
        Assert.NotNull(after.ExistingRating);
        Assert.True(after.CanRate); // editing stays allowed
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static RateAgencyCommand Command(Guid agencyId, Guid raterId)
        => new(agencyId, raterId, 4, 4, 4, 4, null);

    private static UserAccount NewUser() => UserAccount.Create(Guid.NewGuid(), "سارة", "خليل", DateTime.UtcNow);

    private static Agency NewAgency(Guid ownerId)
        => Agency.Create("مكتب الشام", $"sham-{Guid.NewGuid():N}", ownerId, "SY", DateTime.UtcNow);

    private sealed class Fixture
    {
        private readonly Dictionary<Guid, AgencyRating> _store = new();

        public Mock<IAgencyRepository> Agencies { get; } = new();
        public Mock<IAgencyRatingRepository> Ratings { get; } = new();
        public Mock<IUserAccountRepository> Accounts { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public bool HasInteraction { get; set; }
        public RateAgencyCommandHandler Handler { get; }

        public Fixture(Agency agency, params UserAccount[] users)
        {
            Agencies.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => id == agency.Id ? agency : null);
            Accounts.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => users.FirstOrDefault(u => u.Id == id));
            Ratings.Setup(r => r.HasInteractionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => HasInteraction);
            Ratings.Setup(r => r.GetByRaterAndAgencyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, Guid rater, CancellationToken _) => _store.GetValueOrDefault(rater));
            Ratings.Setup(r => r.AddAsync(It.IsAny<AgencyRating>(), It.IsAny<CancellationToken>()))
                .Callback((AgencyRating rating, CancellationToken _) => _store[rating.RaterId] = rating)
                .Returns(Task.CompletedTask);
            Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            Handler = new RateAgencyCommandHandler(Agencies.Object, Ratings.Object, Accounts.Object, Uow.Object);
        }
    }
}
