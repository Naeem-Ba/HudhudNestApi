using Microsoft.Extensions.Options;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.AiContent;
using HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialPublication;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Options;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class CreateSocialPublicationCommandHandlerTests
{
    private static Property MakePublishedPropertyWithImage()
    {
        var property = Property.Create("شقة رائعة في دمشق", "وصف", Guid.NewGuid(), ListingType.ForRent);
        property.City = "دمشق";
        property.Images.Add(new PropertyImage { Url = "https://cdn.example.com/main.jpg", IsMain = true });
        return property;
    }

    private sealed class Fixture
    {
        public Mock<IPropertyRepository> Properties { get; } = new();
        public Mock<ISocialAccountRepository> Accounts { get; } = new();
        public Mock<ISocialChannelRepository> Channels { get; } = new();
        public Mock<ISocialPublicationRepository> Publications { get; } = new();
        public Mock<ISocialPublicationStatusHistoryRepository> History { get; } = new();
        public Mock<ISocialDistributionTargetUrlBuilder> UrlBuilder { get; } = new();
        public ISocialContentGenerator ContentGenerator { get; } = new TemplateSocialContentGenerator();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public CreateSocialPublicationCommandHandler BuildHandler(SocialDistributionContentReviewOptions? contentReview = null) => new(
            Properties.Object, Accounts.Object, Channels.Object, Publications.Object, History.Object, UrlBuilder.Object, ContentGenerator, UnitOfWork.Object,
            logger: null,
            contentReviewOptions: contentReview is null ? null : Options.Create(contentReview));
    }

    private static SocialAccount MakeActiveAccount(Guid channelId, SocialPlatform platform = SocialPlatform.Facebook)
    {
        var account = SocialAccount.Create(channelId, platform, "Page", "ext-1", SocialAccountType.Page);
        account.Connect(null);
        return account;
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesDraftPublicationWithAttachedContent()
    {
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage();
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        var account = MakeActiveAccount(channel.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.UrlBuilder
            .Setup(x => x.BuildAttributedTargetUrl(property.Id, "facebook", "social", "social_distribution", It.IsAny<string>()))
            .Returns("https://realestateworld.world/properties/p1?utm_source=facebook");

        SocialPublication? added = null;
        fixture.Publications.Setup(x => x.AddAsync(It.IsAny<SocialPublication>(), It.IsAny<CancellationToken>()))
            .Callback<SocialPublication, CancellationToken>((p, _) => added = p)
            .Returns(Task.CompletedTask);

        var handler = fixture.BuildHandler();

        var result = await handler.Handle(
            new CreateSocialPublicationCommand(property.Id, account.Id, Guid.NewGuid(), null, null, null, null, "ar"),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(SocialPublicationStatus.Draft, result.Status);
        Assert.NotNull(result.Content);
        Assert.Equal(property.Title, result.Content!.Title);
        Assert.Equal("https://cdn.example.com/main.jpg", result.Content.ImageUrl);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PropertyNotPublic_ThrowsConflict_AndNeverPersists()
    {
        var fixture = new Fixture();
        var propertyId = Guid.NewGuid();
        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync((Property?)null);

        var handler = fixture.BuildHandler();

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateSocialPublicationCommand(propertyId, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, "ar"),
            CancellationToken.None));

        fixture.Publications.Verify(x => x.AddAsync(It.IsAny<SocialPublication>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AccountNotFound_ThrowsNotFound()
    {
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage();
        var accountId = Guid.NewGuid();

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync((SocialAccount?)null);

        var handler = fixture.BuildHandler();

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateSocialPublicationCommand(property.Id, accountId, Guid.NewGuid(), null, null, null, null, "ar"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AccountNotActive_ThrowsConflict()
    {
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage();
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        var inactiveAccount = SocialAccount.Create(channel.Id, SocialPlatform.Facebook, "Page", "ext-1", SocialAccountType.Page);
        // never Connect()-ed: stays PendingAuthorization, CanPublish() == false

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(inactiveAccount.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inactiveAccount);

        var handler = fixture.BuildHandler();

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateSocialPublicationCommand(property.Id, inactiveAccount.Id, Guid.NewGuid(), null, null, null, null, "ar"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ChannelNotActive_ThrowsConflict()
    {
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage();
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        channel.Deactivate();
        var account = MakeActiveAccount(channel.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);

        var handler = fixture.BuildHandler();

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateSocialPublicationCommand(property.Id, account.Id, Guid.NewGuid(), null, null, null, null, "ar"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_PropertyHasNoImage_AndNoImageUrlOverride_ThrowsConflict()
    {
        var fixture = new Fixture();
        var property = Property.Create("شقة بلا صور", "وصف", Guid.NewGuid(), ListingType.ForRent); // no Images added
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        var account = MakeActiveAccount(channel.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);

        var handler = fixture.BuildHandler();

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateSocialPublicationCommand(property.Id, account.Id, Guid.NewGuid(), null, null, null, null, "ar"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ExplicitImageUrlOverride_IsUsedInsteadOfPropertyImage()
    {
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage(); // has a main image too, but override must win
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        var account = MakeActiveAccount(channel.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.UrlBuilder
            .Setup(x => x.BuildAttributedTargetUrl(property.Id, "facebook", "social", "social_distribution", It.IsAny<string>()))
            .Returns("https://realestateworld.world/properties/p1?utm_source=facebook");

        var handler = fixture.BuildHandler();

        var result = await handler.Handle(
            new CreateSocialPublicationCommand(
                property.Id, account.Id, Guid.NewGuid(), "عنوان", "نص", "https://cdn.example.com/override.jpg", null, "ar"),
            CancellationToken.None);

        Assert.Equal("https://cdn.example.com/override.jpg", result.Content!.ImageUrl);
    }

    // ── Content review policy (Phase 1 audit F-11 / SocialDistributionContentReviewOptions) ──

    [Fact]
    public async Task Handle_ReviewPolicyEnabled_AutomaticPublication_ContentIsPendingReview()
    {
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage();
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        var account = MakeActiveAccount(channel.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.UrlBuilder
            .Setup(x => x.BuildAttributedTargetUrl(property.Id, "facebook", "social", "social_distribution", It.IsAny<string>()))
            .Returns("https://realestateworld.world/properties/p1?utm_source=facebook");

        var handler = fixture.BuildHandler(new SocialDistributionContentReviewOptions { RequireReviewForAutomaticPublications = true });

        var result = await handler.Handle(
            // DistributionRuleId set — this is what marks it "automatic" (rule-engine-created).
            new CreateSocialPublicationCommand(property.Id, account.Id, Guid.NewGuid(), null, null, null, null, "ar", DistributionRuleId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ContentReviewStatus.PendingReview, result.Content!.ReviewStatus);
    }

    [Fact]
    public async Task Handle_ReviewPolicyEnabled_ManualPublication_ContentStaysApproved()
    {
        // An admin explicitly calling this endpoint by hand (no DistributionRuleId) is already
        // the deliberate action the review gate exists to add for the unattended path — it must
        // never re-gate a manual creation.
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage();
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        var account = MakeActiveAccount(channel.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.UrlBuilder
            .Setup(x => x.BuildAttributedTargetUrl(property.Id, "facebook", "social", "social_distribution", It.IsAny<string>()))
            .Returns("https://realestateworld.world/properties/p1?utm_source=facebook");

        var handler = fixture.BuildHandler(new SocialDistributionContentReviewOptions { RequireReviewForAutomaticPublications = true });

        var result = await handler.Handle(
            new CreateSocialPublicationCommand(property.Id, account.Id, Guid.NewGuid(), null, null, null, null, "ar"),
            CancellationToken.None);

        Assert.Equal(ContentReviewStatus.Approved, result.Content!.ReviewStatus);
    }

    [Fact]
    public async Task Handle_ReviewPolicyDisabled_AutomaticPublication_ContentStaysApproved()
    {
        // Default/off — today's behavior, unchanged.
        var fixture = new Fixture();
        var property = MakePublishedPropertyWithImage();
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");
        var account = MakeActiveAccount(channel.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.UrlBuilder
            .Setup(x => x.BuildAttributedTargetUrl(property.Id, "facebook", "social", "social_distribution", It.IsAny<string>()))
            .Returns("https://realestateworld.world/properties/p1?utm_source=facebook");

        var handler = fixture.BuildHandler();

        var result = await handler.Handle(
            new CreateSocialPublicationCommand(property.Id, account.Id, Guid.NewGuid(), null, null, null, null, "ar", DistributionRuleId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ContentReviewStatus.Approved, result.Content!.ReviewStatus);
    }
}
