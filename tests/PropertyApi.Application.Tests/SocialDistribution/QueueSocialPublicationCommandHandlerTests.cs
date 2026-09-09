using Moq;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.SocialDistribution.Commands.QueueSocialPublication;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.Tests.SocialDistribution;

public sealed class QueueSocialPublicationCommandHandlerTests
{
    private sealed class Fixture
    {
        public Mock<ISocialPublicationRepository> Publications { get; } = new();
        public Mock<ISocialPublicationStatusHistoryRepository> History { get; } = new();
        public Mock<ISocialAccountRepository> Accounts { get; } = new();
        public Mock<IPropertyRepository> Properties { get; } = new();
        public Mock<ISocialPublicationJobQueue> JobQueue { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public QueueSocialPublicationCommandHandler BuildHandler() => new(
            Publications.Object, History.Object, Accounts.Object, Properties.Object, JobQueue.Object, UnitOfWork.Object);
    }

    private static (SocialPublication publication, SocialAccount account) MakeDraftPublication()
    {
        var account = SocialAccount.Create(Guid.NewGuid(), SocialPlatform.Facebook, "Page", "ext-1", SocialAccountType.Page);
        account.Connect(null);

        var publication = SocialPublication.Create(Guid.NewGuid(), account.Id, Guid.NewGuid(), SocialPlatform.Facebook);
        var content = SocialPostContent.Create(
            publication.Id, SocialPlatform.Facebook, "عنوان", "نص", "https://cdn.example.com/img.jpg",
            "https://realestateworld.world/properties/p1", null, "ar");
        publication.AttachContent(content);

        return (publication, account);
    }

    [Fact]
    public async Task Handle_HappyPath_MovesToQueued()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeDraftPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var result = await fixture.BuildHandler().Handle(
            new QueueSocialPublicationCommand(publication.Id, Guid.NewGuid(), null), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Queued, result.Status);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.JobQueue.Verify(x => x.EnqueueAsync(publication.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PropertyNoLongerPublic_ThrowsConflict()
    {
        var fixture = new Fixture();
        var (publication, _) = MakeDraftPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.BuildHandler().Handle(
            new QueueSocialPublicationCommand(publication.Id, Guid.NewGuid(), null), CancellationToken.None));

        fixture.JobQueue.Verify(x => x.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AccountNoLongerActive_ThrowsConflict()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeDraftPublication();
        account.Suspend();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.BuildHandler().Handle(
            new QueueSocialPublicationCommand(publication.Id, Guid.NewGuid(), null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_PublicationNotFound_ThrowsNotFound()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        fixture.Publications.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((SocialPublication?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.BuildHandler().Handle(
            new QueueSocialPublicationCommand(id, Guid.NewGuid(), null), CancellationToken.None));
    }
}
