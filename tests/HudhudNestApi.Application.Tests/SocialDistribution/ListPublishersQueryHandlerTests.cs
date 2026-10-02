using Moq;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Queries.GetPublisherCapabilities;
using HudhudNestApi.Application.SocialDistribution.Queries.ListPublishers;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>The publisher list is what the admin UI reads to decide which platforms it may offer, so it must say whether each one can really publish.</summary>
public sealed class ListPublishersQueryHandlerTests
{
    private static Mock<ISocialPublisher> Publisher(SocialPlatform platform, bool isLive)
    {
        var publisher = new Mock<ISocialPublisher>();
        publisher.SetupGet(x => x.Platform).Returns(platform);
        publisher.SetupGet(x => x.IsLive).Returns(isLive);
        publisher.Setup(x => x.GetCapabilities()).Returns(new SocialPublisherCapabilities(
            SupportsText: true, SupportsImages: true, SupportsVideo: false, SupportsStories: false,
            SupportsHashtags: true, SupportsScheduling: false, SupportsUpdate: false, SupportsDelete: false));
        return publisher;
    }

    private static Mock<ISocialPublisherRegistry> Registry(params Mock<ISocialPublisher>[] publishers)
    {
        var registry = new Mock<ISocialPublisherRegistry>();
        registry.SetupGet(x => x.SupportedPlatforms).Returns(publishers.Select(p => p.Object.Platform).ToList());
        foreach (var publisher in publishers)
            registry.Setup(x => x.TryGetPublisher(publisher.Object.Platform)).Returns(publisher.Object);
        return registry;
    }

    [Fact]
    public async Task List_ReportsWhetherEachPublisherIsLive()
    {
        var registry = Registry(Publisher(SocialPlatform.Telegram, isLive: true), Publisher(SocialPlatform.TikTok, isLive: false));

        var result = await new ListPublishersQueryHandler(registry.Object).Handle(new ListPublishersQuery(), CancellationToken.None);

        Assert.True(result.Single(p => p.Platform == SocialPlatform.Telegram).IsLive);
        Assert.False(result.Single(p => p.Platform == SocialPlatform.TikTok).IsLive);
    }

    [Fact]
    public async Task Capabilities_ForOnePlatform_AlsoReportsWhetherItIsLive()
    {
        var registry = Registry(Publisher(SocialPlatform.Facebook, isLive: true));

        var result = await new GetPublisherCapabilitiesQueryHandler(registry.Object)
            .Handle(new GetPublisherCapabilitiesQuery(SocialPlatform.Facebook), CancellationToken.None);

        Assert.True(result.IsLive);
    }
}
