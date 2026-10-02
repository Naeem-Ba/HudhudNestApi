using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Options;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class PublishSocialPublicationCommandHandlerTests
{
    private sealed class Fixture
    {
        public Mock<ISocialPublicationRepository> Publications { get; } = new();
        public Mock<ISocialPublicationStatusHistoryRepository> History { get; } = new();
        public Mock<ISocialAccountRepository> Accounts { get; } = new();
        public Mock<IPropertyRepository> Properties { get; } = new();
        public Mock<ISocialPublisherRegistry> Registry { get; } = new();
        public Mock<ISocialMediaAssetGenerator> AssetGenerator { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public PublishSocialPublicationCommandHandler BuildHandler(bool attachGeneratedAssetToAutomaticPublications = false) => new(
            Publications.Object, History.Object, Accounts.Object, Properties.Object,
            Registry.Object, AssetGenerator.Object, UnitOfWork.Object,
            NullLogger<PublishSocialPublicationCommandHandler>.Instance,
            retryOptions: null,
            assetGenerationOptions: Options.Create(new SocialDistributionAssetGenerationOptions
            {
                AttachGeneratedAssetToAutomaticPublications = attachGeneratedAssetToAutomaticPublications,
            }));

        public void RegisterPublisher(ISocialPublisher publisher) =>
            Registry.Setup(x => x.TryGetPublisher(publisher.Platform)).Returns(publisher);
    }

    private sealed class FakePublisher : ISocialPublisher
    {
        public SocialPlatform Platform { get; }
        private readonly Func<SocialPublishRequest, SocialPublishResult> _respond;
        private readonly Func<SocialPublishRequest, SocialContentValidationResult>? _validate;

        public FakePublisher(
            SocialPlatform platform,
            Func<SocialPublishRequest, SocialPublishResult> respond,
            Func<SocialPublishRequest, SocialContentValidationResult>? validate = null)
        {
            Platform = platform;
            _respond = respond;
            _validate = validate;
        }

        public SocialPublisherCapabilities GetCapabilities() => new(
            SupportsText: true, SupportsImages: true, SupportsVideo: false, SupportsStories: false,
            SupportsHashtags: true, SupportsScheduling: false, SupportsUpdate: false, SupportsDelete: false);

        public SocialContentValidationResult ValidateContent(SocialPublishRequest request) =>
            _validate?.Invoke(request) ?? SocialContentValidationResult.Valid;

        public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default) =>
            Task.FromResult(_respond(request));

        public Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default) =>
            Task.FromResult(SocialPublishResult.Failure(SocialPublicationErrorCode.PlatformNotConfigured, "not used in these tests"));

        public Task<SocialPublishResult> CommentAsync(SocialPublishRequest request, string externalPostId, string commentBody, CancellationToken ct = default) =>
            Task.FromResult(SocialPublishResult.Failure(SocialPublicationErrorCode.PlatformNotConfigured, "not used in these tests"));

        public Task<SocialPublishResult> DeleteAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default) =>
            Task.FromResult(SocialPublishResult.Failure(SocialPublicationErrorCode.PlatformNotConfigured, "not used in these tests"));
    }

    private static (SocialPublication publication, SocialAccount account) MakeQueuedPublication(
        SocialPlatform platform = SocialPlatform.Facebook, Guid? distributionRuleId = null,
        string imageUrl = "https://cdn.example.com/img.jpg")
    {
        var channelId = Guid.NewGuid();
        var account = SocialAccount.Create(channelId, platform, "Page", "ext-1", SocialAccountType.Page);
        account.Connect(null);

        var publication = SocialPublication.Create(
            Guid.NewGuid(), account.Id, Guid.NewGuid(), platform, distributionRuleId: distributionRuleId, distributionRunId: distributionRuleId is null ? null : Guid.NewGuid());
        var content = SocialPostContent.Create(
            publication.Id, platform, "عنوان", "نص", imageUrl,
            "https://hudhudnest.com/properties/p1", null, "ar");
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
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, _ => SocialPublishResult.Success("ext-post-1", "https://facebook.com/1")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Published, result.Status);
        Assert.Equal("ext-post-1", result.ExternalPostId);
        // Twice, deliberately (Phase 1 audit F-8): once to persist StartPublishing's
        // Publishing+LeaseUntil transition BEFORE the external call, once for the final outcome.
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.AssetGenerator.Verify(x => x.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PublisherReturnsRetryableFailure_MovesToRetrying()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.RegisterPublisher(new FakePublisher(
            SocialPlatform.Facebook, _ => SocialPublishResult.Failure(SocialPublicationErrorCode.RateLimited, "rate limited")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Retrying, result.Status);
        Assert.Equal(1, result.RetryCount);
    }

    [Fact]
    public async Task Handle_PublisherRejectsTheCredential_MarksTheAccountExpired_SoTheDeadTokenStopsBeingUsed()
    {
        // A revoked/expired token never heals by itself: without this the next queued post for the
        // same account would call the platform again with the same dead credential, forever.
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.RegisterPublisher(new FakePublisher(
            SocialPlatform.Facebook, _ => SocialPublishResult.Failure(SocialPublicationErrorCode.InvalidCredentials, "token rejected")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Failed, result.Status);
        Assert.Equal(SocialAccountStatus.Expired, account.Status);
        fixture.Accounts.Verify(x => x.Update(account), Times.Once);
    }

    [Theory]
    [InlineData(SocialPublicationErrorCode.RateLimited)]
    [InlineData(SocialPublicationErrorCode.NetworkError)]
    [InlineData(SocialPublicationErrorCode.PermissionDenied)]
    [InlineData(SocialPublicationErrorCode.InvalidContent)]
    public async Task Handle_AnyOtherPublisherFailure_LeavesTheAccountActive(SocialPublicationErrorCode errorCode)
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, _ => SocialPublishResult.Failure(errorCode, "failed")));

        await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialAccountStatus.Active, account.Status);
        fixture.Accounts.Verify(x => x.Update(It.IsAny<SocialAccount>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PublisherReturnsNonRetryableFailure_MovesToFailed()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.RegisterPublisher(new FakePublisher(
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
        // No publisher registered for Facebook on purpose — Registry.TryGetPublisher returns null by default.

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
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, _ =>
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
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, _ => { publisherCalled = true; return SocialPublishResult.Success("x"); }));

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
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, _ => throw new InvalidOperationException("boom")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        // Retryable (NetworkError) — an unexpected publisher exception should not permanently fail
        // the publication on the first attempt.
        Assert.Equal(SocialPublicationStatus.Retrying, result.Status);
        Assert.DoesNotContain("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_InvalidContentPerPublisherValidation_MarksFailed_InvalidContent_WithoutCallingPublishAsync()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication();
        var publishCalled = false;

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.RegisterPublisher(new FakePublisher(
            SocialPlatform.Facebook,
            respond: _ => { publishCalled = true; return SocialPublishResult.Success("x"); },
            validate: _ => SocialContentValidationResult.Invalid("النص يتجاوز الحد المسموح.")));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Failed, result.Status);
        Assert.Equal(SocialPublicationErrorCode.InvalidContent, result.ErrorCode);
        Assert.False(publishCalled);
    }

    [Fact]
    public async Task Handle_ByDefault_RuleEngineCreatedPublication_NeverGeneratesAnAsset_PublishesWithThePropertysOwnPhoto()
    {
        // Phase 1 audit F-4: the generator only ever produces image/svg+xml, which no real target
        // platform accepts — with AttachGeneratedAssetToAutomaticPublications left at its default
        // (false), an automatic publication must publish with the real property photo its content
        // already carries (MakeQueuedPublication's content), never call the asset generator at all.
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication(distributionRuleId: Guid.NewGuid());
        var originalImageUrl = publication.Content!.ImageUrl;

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        SocialPublishRequest? capturedRequest = null;
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, req =>
        {
            capturedRequest = req;
            return SocialPublishResult.Success("ext-1");
        }));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Published, result.Status);
        Assert.Equal(originalImageUrl, capturedRequest!.ImageUrl);
        fixture.AssetGenerator.Verify(x => x.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CloudinaryPhoto_IsSentToThePublisherAsABoundedJpeg_WhileTheStoredContentKeepsTheOriginal()
    {
        // Instagram accepts JPEG only and Telegram's sendPhoto-by-URL caps the file at 5 MB, but
        // the owner's upload can be a large PNG/WebP/HEIC: normalise at publish time only — the
        // stored SocialPostContent (what the admin sees and what a retry rebuilds from) is untouched.
        const string original = "https://res.cloudinary.com/demo/image/upload/v1/property-images/abc.png";
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication(imageUrl: original);

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        SocialPublishRequest? capturedRequest = null;
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, req =>
        {
            capturedRequest = req;
            return SocialPublishResult.Success("ext-1");
        }));

        var result = await fixture.BuildHandler().Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Published, result.Status);
        Assert.Equal(
            "https://res.cloudinary.com/demo/image/upload/f_jpg,q_auto,w_1440,c_limit/v1/property-images/abc.jpg",
            capturedRequest!.ImageUrl);
        Assert.Equal(original, publication.Content!.ImageUrl);
    }

    [Fact]
    public async Task Handle_AssetGenerationEnabled_RuleEngineCreatedPublication_GeneratesAndAttachesAsset_BeforePublishing()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication(distributionRuleId: Guid.NewGuid());
        var generatedAssetId = Guid.NewGuid();

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.AssetGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedSocialAssetResult(
                generatedAssetId, SocialPlatform.Facebook, SocialAssetType.FeedImage,
                "https://cdn.example.com/generated.svg", 1200, 630, "image/svg+xml", 2048, "abc123", "default", 1, Reused: false));

        SocialPublishRequest? capturedRequest = null;
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, req =>
        {
            capturedRequest = req;
            return SocialPublishResult.Success("ext-1");
        }));

        var result = await fixture.BuildHandler(attachGeneratedAssetToAutomaticPublications: true)
            .Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Published, result.Status);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://cdn.example.com/generated.svg", capturedRequest!.ImageUrl);
        fixture.AssetGenerator.Verify(x => x.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AssetGenerationEnabled_AssetGenerationFailsRetryable_MovesToRetrying_WithoutCallingPublisher()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication(distributionRuleId: Guid.NewGuid());
        var publisherCalled = false;

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.AssetGenerator
            .Setup(x => x.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocialAssetGenerationException("تعذّر الرفع.", retryable: true));
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, _ => { publisherCalled = true; return SocialPublishResult.Success("x"); }));

        var result = await fixture.BuildHandler(attachGeneratedAssetToAutomaticPublications: true)
            .Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Retrying, result.Status);
        Assert.False(publisherCalled);
    }

    [Fact]
    public async Task Handle_ManuallyCreatedPublication_NeverTriggersAssetGeneration()
    {
        var fixture = new Fixture();
        var (publication, account) = MakeQueuedPublication(distributionRuleId: null);

        fixture.Publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Properties.Setup(x => x.IsPubliclyVisibleAsync(publication.PropertyId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fixture.RegisterPublisher(new FakePublisher(SocialPlatform.Facebook, _ => SocialPublishResult.Success("ext-1")));

        // Even with the flag enabled, a manually-created publication (no DistributionRuleId) must
        // never trigger asset generation — see the handler's own remarks.
        await fixture.BuildHandler(attachGeneratedAssetToAutomaticPublications: true)
            .Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None);

        fixture.AssetGenerator.Verify(x => x.GenerateAsync(It.IsAny<GenerateSocialAssetRequest>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
