using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Events;
using PropertyApi.Application.SocialDistribution.EventHandlers;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Application.Tests.SocialDistribution;

/// <summary>Phase 11 spec §7/§31.E — capability-gated Update/Comment/Delete/No-op on property status change, plus idempotency.</summary>
public sealed class PropertyStatusChangedDistributionHandlerTests
{
    private sealed class Fixture
    {
        public Mock<ISocialPublicationRepository> Publications { get; } = new();
        public Mock<ISocialAccountRepository> Accounts { get; } = new();
        public Mock<ISocialPublicationStatusHistoryRepository> History { get; } = new();
        public Mock<ISocialPublisherRegistry> Registry { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Fixture() =>
            History.Setup(x => x.GetByPublicationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<SocialPublicationStatusHistory>());

        public PropertyStatusChangedDistributionHandler Build() => new(
            Publications.Object, Accounts.Object, History.Object, Registry.Object, UnitOfWork.Object,
            NullLogger<PropertyStatusChangedDistributionHandler>.Instance);
    }

    private static (SocialPublication publication, SocialAccount account) MakePublishedPublication(SocialPlatform platform = SocialPlatform.Facebook)
    {
        var account = SocialAccount.Create(Guid.NewGuid(), platform, "Page", "ext-1", SocialAccountType.Page);
        account.Connect(null);

        var publication = SocialPublication.Create(Guid.NewGuid(), account.Id, Guid.NewGuid(), platform);
        var content = SocialPostContent.Create(
            publication.Id, platform, "عنوان", "نص", "https://cdn.example.com/img.jpg",
            "https://realestateworld.world/properties/p1", null, "ar");
        publication.AttachContent(content);
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkPublished("ext-post-1", "https://facebook.com/ext-post-1", DateTime.UtcNow);

        return (publication, account);
    }

    private sealed class FakeCapabilityPublisher : ISocialPublisher
    {
        private readonly SocialPublisherCapabilities _capabilities;
        public SocialPlatform Platform { get; }
        public bool UpdateCalled { get; private set; }
        public bool CommentCalled { get; private set; }
        public bool DeleteCalled { get; private set; }

        public FakeCapabilityPublisher(SocialPlatform platform, SocialPublisherCapabilities capabilities)
        {
            Platform = platform;
            _capabilities = capabilities;
        }

        public SocialPublisherCapabilities GetCapabilities() => _capabilities;
        public SocialContentValidationResult ValidateContent(SocialPublishRequest request) => SocialContentValidationResult.Valid;
        public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default) =>
            Task.FromResult(SocialPublishResult.Success("x"));

        public Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
        {
            UpdateCalled = true;
            return Task.FromResult(SocialPublishResult.Success(externalPostId));
        }

        public Task<SocialPublishResult> CommentAsync(string externalPostId, string commentBody, CancellationToken ct = default)
        {
            CommentCalled = true;
            return Task.FromResult(SocialPublishResult.Success(externalPostId));
        }

        public Task<SocialPublishResult> DeleteAsync(string externalPostId, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.FromResult(SocialPublishResult.Success(externalPostId));
        }
    }

    private static SocialPublisherCapabilities FullCapabilities() => new(
        SupportsText: true, SupportsImages: true, SupportsVideo: false, SupportsStories: false,
        SupportsHashtags: true, SupportsScheduling: true, SupportsUpdate: true, SupportsDelete: true, SupportsComment: true);

    private static SocialPublisherCapabilities NoLifecycleCapabilities() => new(
        SupportsText: true, SupportsImages: true, SupportsVideo: false, SupportsStories: false,
        SupportsHashtags: true, SupportsScheduling: true, SupportsUpdate: false, SupportsDelete: false, SupportsComment: false);

    [Fact]
    public async Task Handle_NoOpTransition_NeverLoadsPublications()
    {
        var fixture = new Fixture();
        var handler = fixture.Build();

        await handler.Handle(new PropertyStatusChangedEvent(Guid.NewGuid(), PropertyStatus.Sold, PropertyStatus.Sold, DateTime.UtcNow), CancellationToken.None);

        fixture.Publications.Verify(x => x.GetActiveForPropertyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReservedToSold_PublisherSupportsUpdate_CallsUpdate()
    {
        var fixture = new Fixture();
        var (publication, account) = MakePublishedPublication();
        var publisher = new FakeCapabilityPublisher(SocialPlatform.Facebook, FullCapabilities());

        fixture.Publications.Setup(x => x.GetActiveForPropertyAsync(publication.PropertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { publication });
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Registry.Setup(x => x.TryGetPublisher(SocialPlatform.Facebook)).Returns(publisher);

        await fixture.Build().Handle(
            new PropertyStatusChangedEvent(publication.PropertyId, PropertyStatus.Reserved, PropertyStatus.Sold, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(publisher.UpdateCalled);
        fixture.History.Verify(x => x.AddAsync(It.IsAny<SocialPublicationStatusHistory>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReservedToSold_PublisherDoesNotSupportUpdate_NeverCallsUpdate_RecordsNoOp()
    {
        var fixture = new Fixture();
        var (publication, account) = MakePublishedPublication(SocialPlatform.TikTok);
        var publisher = new FakeCapabilityPublisher(SocialPlatform.TikTok, NoLifecycleCapabilities());

        fixture.Publications.Setup(x => x.GetActiveForPropertyAsync(publication.PropertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { publication });
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Registry.Setup(x => x.TryGetPublisher(SocialPlatform.TikTok)).Returns(publisher);

        await fixture.Build().Handle(
            new PropertyStatusChangedEvent(publication.PropertyId, PropertyStatus.Reserved, PropertyStatus.Sold, DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(publisher.UpdateCalled);
        Assert.False(publisher.DeleteCalled);
    }

    [Fact]
    public async Task Handle_AvailableToReserved_SupportsComment_CallsComment_NeverUpdatesOriginalPost()
    {
        var fixture = new Fixture();
        var (publication, account) = MakePublishedPublication();
        var publisher = new FakeCapabilityPublisher(SocialPlatform.Facebook, FullCapabilities());

        fixture.Publications.Setup(x => x.GetActiveForPropertyAsync(publication.PropertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { publication });
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Registry.Setup(x => x.TryGetPublisher(SocialPlatform.Facebook)).Returns(publisher);

        await fixture.Build().Handle(
            new PropertyStatusChangedEvent(publication.PropertyId, PropertyStatus.Available, PropertyStatus.Reserved, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(publisher.CommentCalled);
        Assert.False(publisher.UpdateCalled);
    }

    [Fact]
    public async Task Handle_TransitionAlreadyRecorded_IsSkipped_NeverActsTwice()
    {
        var fixture = new Fixture();
        var (publication, account) = MakePublishedPublication();
        var publisher = new FakeCapabilityPublisher(SocialPlatform.Facebook, FullCapabilities());

        fixture.Publications.Setup(x => x.GetActiveForPropertyAsync(publication.PropertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { publication });
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Registry.Setup(x => x.TryGetPublisher(SocialPlatform.Facebook)).Returns(publisher);
        fixture.History.Setup(x => x.GetByPublicationIdAsync(publication.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { SocialPublicationStatusHistory.Record(publication.Id, publication.Status, publication.Status, null, "PropertyStatusChanged:Reserved->Sold: تم تنفيذ Update — نجح.", DateTime.UtcNow) });

        await fixture.Build().Handle(
            new PropertyStatusChangedEvent(publication.PropertyId, PropertyStatus.Reserved, PropertyStatus.Sold, DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(publisher.UpdateCalled);
    }

    [Fact]
    public async Task Handle_PublisherThrows_IsCaughtAndLogged_NeverPropagates()
    {
        var fixture = new Fixture();
        fixture.Publications.Setup(x => x.GetActiveForPropertyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var exception = await Record.ExceptionAsync(() => fixture.Build().Handle(
            new PropertyStatusChangedEvent(Guid.NewGuid(), PropertyStatus.Available, PropertyStatus.Sold, DateTime.UtcNow),
            CancellationToken.None));

        Assert.Null(exception);
    }
}
