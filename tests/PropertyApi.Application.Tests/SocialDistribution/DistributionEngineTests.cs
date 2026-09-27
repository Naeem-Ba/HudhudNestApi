using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.SocialDistribution.Commands.CreateSocialPublication;
using PropertyApi.Application.SocialDistribution.Commands.QueueSocialPublication;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Options;
using PropertyApi.Application.SocialDistribution.Services;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.Tests.SocialDistribution;

public sealed class DistributionEngineTests
{
    private sealed class Fixture
    {
        public Mock<IPropertyRepository> Properties { get; } = new();
        public Mock<IDistributionRuleRepository> Rules { get; } = new();
        public Mock<IDistributionRunRepository> Runs { get; } = new();
        public Mock<ISocialAccountRepository> Accounts { get; } = new();
        public Mock<ISocialChannelRepository> Channels { get; } = new();
        public Mock<ISocialPublicationRepository> Publications { get; } = new();
        public Mock<ISender> Sender { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Fixture()
        {
            // Every DistributionRun mutation just needs to "persist" without a real database.
            Runs.Setup(x => x.AddAsync(It.IsAny<DistributionRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Publications.Setup(x => x.ExistsActiveForPropertyAndAccountAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        public DistributionEngine BuildEngine(SocialDistributionEligibilityOptions? eligibility = null) => new(
            Properties.Object, Rules.Object, Runs.Object, Accounts.Object, Channels.Object, Publications.Object,
            Sender.Object, UnitOfWork.Object, NullLogger<DistributionEngine>.Instance,
            eligibility is null ? null : Options.Create(eligibility));
    }

    private static Property MakePublishedProperty(int? governorateId = 1, int? propertyTypeId = 2, ListingType listingType = ListingType.ForSale)
    {
        // A real published property always clears the eligibility gate
        // (SocialDistributionEligibilityOptions' defaults) — PublishPropertyCommandHandler itself
        // already refuses to publish a listing with zero images, so a fixture with none was never
        // a realistic "published" property to begin with.
        var property = Property.Create("عقار للبيع في دمشق", "وصف كامل وتفصيلي لهذا العقار يشرح مساحته وموقعه", Guid.NewGuid(), listingType);
        property.GovernorateId = governorateId;
        property.PropertyTypeId = propertyTypeId;
        property.Images.Add(new PropertyImage { Url = "https://cdn.example.test/main.jpg", IsMain = true });
        return property;
    }

    private static (SocialAccount account, SocialChannel channel) MakeActiveAccount(SocialPlatform platform = SocialPlatform.Facebook)
    {
        var channel = SocialChannel.Create(platform, platform.ToString());
        var account = SocialAccount.Create(channel.Id, platform, $"{platform} Page", Guid.NewGuid().ToString(), SocialAccountType.Page);
        account.Connect(null);
        return (account, channel);
    }

    private static SocialPublicationDto MakeDto(Guid propertyId, Guid accountId) => new(
        Guid.NewGuid(), propertyId, accountId, SocialPublicationStatus.Draft,
        null, null, null, null, null, null, null, null, null,
        0, 5, null, null,
        "facebook", "social", "social_distribution", "publication_x",
        null, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task RunAsync_PropertyNotPublic_ThrowsConflict()
    {
        var fixture = new Fixture();
        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Property?)null);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.BuildEngine().RunAsync(Guid.NewGuid(), DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_NoMatchingRules_CompletesWithZeroCounts()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<ListingType>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DistributionRule>());

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(DistributionRunStatus.Completed, run.Status);
        Assert.Equal(0, run.MatchedRuleCount);
        Assert.Equal(0, run.PublicationsCreatedCount);
        fixture.Sender.Verify(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_OneMatchingRule_EligibleAccount_CreatesAndQueuesOnePublication()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        var (account, channel) = MakeActiveAccount();
        var rule = DistributionRule.Create("قاعدة", null, 1, 2, ListingType.ForSale, account.Id, 100, null, null, null);
        var dto = MakeDto(property.Id, account.Id);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([rule]);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.Sender.Setup(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        fixture.Sender.Setup(x => x.Send(It.IsAny<QueueSocialPublicationCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(DistributionRunStatus.Completed, run.Status);
        Assert.Equal(1, run.MatchedRuleCount);
        Assert.Equal(1, run.PublicationsCreatedCount);
        Assert.Equal(0, run.SkippedCount);

        fixture.Sender.Verify(x => x.Send(
            It.Is<CreateSocialPublicationCommand>(c => c.SocialAccountId == account.Id && c.DistributionRuleId == rule.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Sender.Verify(x => x.Send(It.IsAny<QueueSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_MultipleMatchingAccounts_CreatesOnePublicationPerAccount()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        var (facebook, facebookChannel) = MakeActiveAccount(SocialPlatform.Facebook);
        var (instagram, instagramChannel) = MakeActiveAccount(SocialPlatform.Instagram);
        var (telegram, telegramChannel) = MakeActiveAccount(SocialPlatform.Telegram);

        var ruleFacebook = DistributionRule.Create("FB", null, 1, 2, ListingType.ForSale, facebook.Id, 100, null, null, null);
        var ruleInstagram = DistributionRule.Create("IG", null, 1, 2, ListingType.ForSale, instagram.Id, 100, null, null, null);
        var ruleTelegram = DistributionRule.Create("TG", null, 1, 2, ListingType.ForSale, telegram.Id, 100, null, null, null);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ruleFacebook, ruleInstagram, ruleTelegram]);

        foreach (var (acc, ch) in new[] { (facebook, facebookChannel), (instagram, instagramChannel), (telegram, telegramChannel) })
        {
            fixture.Accounts.Setup(x => x.GetByIdAsync(acc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(acc);
            fixture.Channels.Setup(x => x.GetByIdAsync(ch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ch);
            fixture.Sender.Setup(x => x.Send(It.Is<CreateSocialPublicationCommand>(c => c.SocialAccountId == acc.Id), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeDto(property.Id, acc.Id));
        }
        fixture.Sender.Setup(x => x.Send(It.IsAny<QueueSocialPublicationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueSocialPublicationCommand cmd, CancellationToken _) => MakeDto(property.Id, Guid.NewGuid()));

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(3, run.PublicationsCreatedCount);
        Assert.Equal(DistributionRunStatus.Completed, run.Status);
        fixture.Sender.Verify(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RunAsync_TwoRulesTargetSameAccount_CreatesOnlyOnePublication_UsingHigherPriorityRule()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        var (account, channel) = MakeActiveAccount();

        var generalRule = DistributionRule.Create("عام", null, null, null, null, account.Id, 10, null, null, null);
        var specificRule = DistributionRule.Create("خاص", null, 1, 2, ListingType.ForSale, account.Id, 999, null, null, null);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([generalRule, specificRule]);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.Sender.Setup(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDto(property.Id, account.Id));
        fixture.Sender.Setup(x => x.Send(It.IsAny<QueueSocialPublicationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDto(property.Id, account.Id));

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(1, run.PublicationsCreatedCount);
        // Both rules matched (spec §14: MatchedRuleCount counts distinct matched rules, not winners).
        Assert.Equal(2, run.MatchedRuleCount);

        fixture.Sender.Verify(x => x.Send(
            It.Is<CreateSocialPublicationCommand>(c => c.DistributionRuleId == specificRule.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_InactiveAccount_IsSkipped_DoesNotCallSender()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        var (account, channel) = MakeActiveAccount();
        account.Deactivate();
        var rule = DistributionRule.Create("قاعدة", null, 1, 2, ListingType.ForSale, account.Id, 100, null, null, null);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([rule]);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(0, run.PublicationsCreatedCount);
        Assert.Equal(1, run.SkippedCount);
        Assert.Equal(DistributionRunStatus.Failed, run.Status); // matched something, nothing eligible
        fixture.Sender.Verify(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_InactiveChannel_IsSkipped()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        var (account, channel) = MakeActiveAccount();
        channel.Deactivate();
        var rule = DistributionRule.Create("قاعدة", null, 1, 2, ListingType.ForSale, account.Id, 100, null, null, null);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([rule]);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(0, run.PublicationsCreatedCount);
        Assert.Equal(1, run.SkippedCount);
        fixture.Sender.Verify(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_DuplicateActivePublicationAlreadyExists_IsSkipped_Idempotent()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        var (account, channel) = MakeActiveAccount();
        var rule = DistributionRule.Create("قاعدة", null, 1, 2, ListingType.ForSale, account.Id, 100, null, null, null);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([rule]);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);
        fixture.Publications.Setup(x => x.ExistsActiveForPropertyAndAccountAsync(property.Id, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(0, run.PublicationsCreatedCount);
        Assert.Equal(1, run.SkippedCount);
        fixture.Sender.Verify(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreviewAsync_DoesNotCreateARunOrCallSender()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        var (account, channel) = MakeActiveAccount();
        var rule = DistributionRule.Create("قاعدة", null, 1, 2, ListingType.ForSale, account.Id, 100, null, null, null);

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([rule]);
        fixture.Accounts.Setup(x => x.GetByIdAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Channels.Setup(x => x.GetByIdAsync(channel.Id, It.IsAny<CancellationToken>())).ReturnsAsync(channel);

        var preview = await fixture.BuildEngine().PreviewAsync(property.Id, CancellationToken.None);

        Assert.Equal(1, preview.MatchedRuleCount);
        var target = Assert.Single(preview.Targets);
        Assert.True(target.WouldPublish);
        Assert.Null(target.SkipReason);

        fixture.Runs.Verify(x => x.AddAsync(It.IsAny<DistributionRun>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Sender.Verify(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreviewAsync_PropertyNotPublic_ThrowsConflict()
    {
        var fixture = new Fixture();
        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Property?)null);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.BuildEngine().PreviewAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ── Eligibility gate (Phase 1 audit F-11 / SocialDistributionEligibilityOptions) ─────────

    [Fact]
    public async Task RunAsync_PropertyWithNoImages_IsIneligible_CompletesTheRunWithoutEvaluatingRules()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        property.Images.Clear();

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);

        var run = await fixture.BuildEngine().RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Equal(0, run.MatchedRuleCount);
        Assert.Equal(0, run.PublicationsCreatedCount);
        Assert.Contains("PropertyIneligible", run.ResultReason);
        // The gate runs BEFORE rule evaluation — an ineligible listing must never even query rules.
        fixture.Rules.Verify(x => x.GetActiveCandidatesAsync(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<ListingType>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_DescriptionShorterThanConfiguredMinimum_IsIneligible()
    {
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        property.UpdateDescription("قصير");

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);

        var run = await fixture.BuildEngine(new SocialDistributionEligibilityOptions { MinDescriptionLength = 50 })
            .RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.Contains("PropertyIneligible", run.ResultReason);
    }

    [Fact]
    public async Task RunAsync_ConfiguredZeroMinimums_NeverBlocksAnEligibleProperty()
    {
        // The gate is entirely opt-out via configuration — an owner who disagrees with the
        // defaults sets both to 0 and gets today's unconditional behavior back.
        var fixture = new Fixture();
        var property = MakePublishedProperty();
        property.Images.Clear();
        property.UpdateDescription("قصير جداً");

        fixture.Properties.Setup(x => x.GetPublishedByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Rules.Setup(x => x.GetActiveCandidatesAsync(1, 2, ListingType.ForSale, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var run = await fixture.BuildEngine(new SocialDistributionEligibilityOptions { MinImageCount = 0, MinDescriptionLength = 0 })
            .RunAsync(property.Id, DistributionRunTriggerType.PropertyPublished, null, CancellationToken.None);

        Assert.DoesNotContain("PropertyIneligible", run.ResultReason ?? string.Empty);
        fixture.Rules.Verify(x => x.GetActiveCandidatesAsync(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<ListingType>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
