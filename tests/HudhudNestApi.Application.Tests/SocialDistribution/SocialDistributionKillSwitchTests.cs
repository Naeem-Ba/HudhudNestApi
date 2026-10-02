using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.DispatchPropertyDistribution;
using HudhudNestApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using HudhudNestApi.Application.SocialDistribution.EventHandlers;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Options;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>
/// <c>SocialDistribution:Enabled=false</c> is the operator's "stop everything now": no new
/// publication is created (event, reconciliation, manual dispatch), no queued one is sent (worker,
/// manual publish), and no lifecycle call (edit/comment/delete) leaves the process. Each guard is
/// pinned here at its own choke point; the worker and the real-database behaviour are covered by
/// the end-to-end suite.
/// </summary>
public sealed class SocialDistributionKillSwitchTests
{
    private sealed class FakeSwitch(bool enabled) : ISocialDistributionSwitch
    {
        public bool IsEnabled { get; set; } = enabled;
    }

    [Fact]
    public async Task ManualPublish_WhenDisabled_IsRefused_BeforeAnythingIsTouched()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var accounts = new Mock<ISocialAccountRepository>();
        var registry = new Mock<ISocialPublisherRegistry>();
        var uow = new Mock<IUnitOfWork>();

        var account = SocialAccount.Create(Guid.NewGuid(), SocialPlatform.Telegram, "Channel", "@hudhudnest", SocialAccountType.Channel);
        account.Connect(null);
        var publication = SocialPublication.Create(Guid.NewGuid(), account.Id, Guid.NewGuid(), SocialPlatform.Telegram);
        publication.AttachContent(SocialPostContent.Create(
            publication.Id, SocialPlatform.Telegram, "عنوان", "نص", "https://cdn.example.com/img.jpg", "https://hudhudnest.com/properties/p1", null, "ar"));
        publication.Queue(null, DateTime.UtcNow);
        publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);

        var handler = new PublishSocialPublicationCommandHandler(
            publications.Object, new Mock<ISocialPublicationStatusHistoryRepository>().Object, accounts.Object,
            new Mock<IPropertyRepository>().Object, registry.Object, new Mock<ISocialMediaAssetGenerator>().Object, uow.Object,
            NullLogger<PublishSocialPublicationCommandHandler>.Instance,
            distributionSwitch: new FakeSwitch(enabled: false));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new PublishSocialPublicationCommand(publication.Id), CancellationToken.None));

        Assert.Equal(SocialPublicationStatus.Queued, publication.Status); // never moved to Publishing
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        registry.Verify(x => x.TryGetPublisher(It.IsAny<SocialPlatform>()), Times.Never);
    }

    [Fact]
    public async Task ManualDispatch_WhenDisabled_IsRefused_AndNeverRunsTheEngine()
    {
        var engine = new Mock<IDistributionEngine>();
        var handler = new DispatchPropertyDistributionCommandHandler(engine.Object, new FakeSwitch(enabled: false));

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new DispatchPropertyDistributionCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        engine.Verify(
            x => x.RunAsync(It.IsAny<Guid>(), It.IsAny<DistributionRunTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PropertyPublished_WhenDisabled_NeverRunsTheEngine()
    {
        var engine = new Mock<IDistributionEngine>();
        var handler = new PropertyPublishedDistributionHandler(
            engine.Object, NullLogger<PropertyPublishedDistributionHandler>.Instance, new FakeSwitch(enabled: false));

        await handler.Handle(new PropertyPublishedEvent(Guid.NewGuid(), DateTime.UtcNow, null), CancellationToken.None);

        engine.Verify(
            x => x.RunAsync(It.IsAny<Guid>(), It.IsAny<DistributionRunTriggerType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LifecycleEvents_WhenDisabled_NeverLoadPublications_SoNoPlatformCallLeavesTheProcess()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var handler = new PropertyStatusChangedDistributionHandler(
            publications.Object, new Mock<ISocialAccountRepository>().Object, new Mock<ISocialPublicationStatusHistoryRepository>().Object,
            new Mock<ISocialPublisherRegistry>().Object, new Mock<IUnitOfWork>().Object,
            NullLogger<PropertyStatusChangedDistributionHandler>.Instance,
            distributionSwitch: new FakeSwitch(enabled: false));

        await handler.Handle(
            new PropertyStatusChangedEvent(Guid.NewGuid(), PropertyStatus.Available, PropertyStatus.Sold, DateTime.UtcNow), CancellationToken.None);
        await handler.Handle(
            new PropertyDeletedEvent(Guid.NewGuid(), PropertyStatus.Available, DateTime.UtcNow, null), CancellationToken.None);

        publications.Verify(x => x.GetActiveForPropertyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfiguredSwitch_FollowsTheLiveConfigurationValue(bool enabled)
    {
        var options = new Mock<IOptionsMonitor<SocialDistributionSwitchOptions>>();
        options.Setup(x => x.CurrentValue).Returns(new SocialDistributionSwitchOptions { Enabled = enabled });

        Assert.Equal(enabled, new ConfiguredSocialDistributionSwitch(options.Object).IsEnabled);
    }

    [Fact]
    public void Options_DefaultToEnabled_SoNothingChangesUntilAnOperatorFlipsIt() =>
        Assert.True(new SocialDistributionSwitchOptions().Enabled);

    [Fact]
    public async Task WhenEnabled_TheGuardsStayOutOfTheWay()
    {
        var engine = new Mock<IDistributionEngine>();
        var handler = new PropertyPublishedDistributionHandler(
            engine.Object, NullLogger<PropertyPublishedDistributionHandler>.Instance, new FakeSwitch(enabled: true));

        await handler.Handle(new PropertyPublishedEvent(Guid.NewGuid(), DateTime.UtcNow, null), CancellationToken.None);

        engine.Verify(
            x => x.RunAsync(It.IsAny<Guid>(), DistributionRunTriggerType.PropertyPublished, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
