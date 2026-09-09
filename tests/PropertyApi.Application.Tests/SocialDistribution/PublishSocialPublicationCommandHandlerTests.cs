using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Application.Tests.SocialDistribution;

public sealed class PublishSocialPublicationCommandHandlerTests
{
    private sealed class Fixture
    {
        public Mock<ISocialPublicationRepository> Publications { get; } = new();
        public Mock<ISocialPublicationStatusHistoryRepository> History { get; } = new();
        public Mock<ISocialAccountRepository> Accounts { get; } = new();
        public Mock<IPropertyRepository> Properties { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<ISocialPublisher> Publishers { get; } = new();

        public PublishSocialPublicationCommandHandler BuildHandler() => new(
            Publications.Object, History.Object, Accounts.Object, Properties.Object, Publishers, UnitOfWork.Object, NullLogger<PublishSocialPublicationCommandHandler>.Instance);
    }

    private sealed class FakePublisher : ISocialPublisher
    {
        public SocialPlatform Platform { get; }
        private readonly Func<SocialPublishRequest, SocialPublishResult> _respond;

        public FakePublisher(SocialPlatform platform, Func<SocialPublishRequest, SocialPublishResult> respond)
        {
            Platform = platform;
            _respond = respond;
        }

        public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default) =>
            Task.FromResult(_respond(request));
    }

    private static (SocialPublication publication, SocialAccount account) MakeQueuedPublication(SocialPlatform platform = SocialPlatform.Facebook)
    {
        var channelId = Guid.NewGuid();
        var account = SocialAccount.Create(channelId, platform, "Page", "ext-1", SocialAccountType.Page);
        account.Connect(null);

        var publication = SocialPublication.Create(Guid.NewGuid(), account.Id, Guid.NewGuid(), platform);
        var content = SocialPostContent.Create(
            publication.Id, platform, "عنوان", "نص", "https://cdn.example.com/img.jpg",
            "https://realestateworld.world/properties/p1", null, "ar");
        publication.AttachContent(content);
        publication.Queue(null, DateTime.UtcNow);

        return (publication, account);
    }

    [Fact]
    public async Task Handle_PublisherSucceeds_MarksPublished()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.Publishers.Add(new FakePublisher(SocialPlatform.Facebook, _ => SocialPublishResult.Success("ext-post-1", "https://facebook.com/1")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Published, result.Status);
        Assert.Equal("ext-post-1", result.ExternalPostId);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PublisherReturnsRetryableFailure_MovesToRetrying()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.Publishers.Add(new FakePublisher(
            SocialPlatform.Facebook, _ => SocialPublishResult.Failure(SocialPublicationErrorCode.RateLimited, "rate limited")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Retrying, result.Status);
        Assert.Equal(1, result.RetryCount);
    }

    [Fact]
    public async Task Handle_PublisherReturnsNonRetryableFailure_MovesToFailed()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.Publishers.Add(new FakePublisher(
            SocialPlatform.Facebook, _ => SocialPublishResult.Failure(SocialPublicationErrorCode.PermissionDenied, "no permission")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Failed, result.Status);
        Assert.Equal(0, result.RetryCount);
    }

    [Fact]
    public async Task Handle_NoPublisherRegisteredForPlatform_MarksFailed_PlatformNotConfigured()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();
        // fixture.Publishers left empty on purpose.

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Failed, result.Status);
        Assert.Equal(SocialPublicationErrorCode.PlatformNotConfigured, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_PropertyNoLongerPublicAtExecutionTime_MarksFailed_PropertyNotPublic_WithoutCallingPublisher()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();
        var publisherCalled = false;

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fixture.Publishers.Add(new FakePublisher(SocialPlatform.Facebook, _ =>
        {
            publisherCalled = true;
            return SocialPublishResult.Success("should-not-happen");
        }));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Failed, result.Status);
        Assert.Equal(SocialPublicationErrorCode.PropertyNotPublic, result.ErrorCode);
        Assert.False(publisherCalled);
    }

    [Fact]
    public async Task Handle_AccountNoLongerActiveAtExecutionTime_MarksFailed_PermissionDenied()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();
        account.Suspend();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Failed, result.Status);
        Assert.Equal(SocialPublicationErrorCode.PermissionDenied, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_PublicationAlreadyPublished_ThrowsInvalidStateTransition_WithoutCallingPublisher()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();
        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkPublished("ext-1", null, DateTime.UtcNow);

        var publisherCalled = false;
        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Publishers.Add(new FakePublisher(SocialPlatform.Facebook, _ => { publisherCalled = true; return SocialPublishResult.Success("x"); }));

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None));

        Assert.False(publisherCalled);
    }

    [Fact]
    public async Task Handle_PublicationNotFound_ThrowsNotFound()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        fixture.Publications.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((SocialPublication?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_PublisherThrows_IsCaughtAndRecordedAsNetworkErrorFailure_NeverPropagates()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.Publishers.Add(new FakePublisher(SocialPlatform.Facebook, _ => throw new InvalidOperationException("boom")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        // Retryable (NetworkError) — an unexpected publisher exception should not permanently fail
        // the publication on the first attempt.
        Assert.Equal(SocialPublicationStatus.Retrying, result.Status);
        Assert.DoesNotContain("boom", result.ErrorMessage);
    }
}
