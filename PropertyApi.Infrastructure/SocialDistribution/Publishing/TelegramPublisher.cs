using Microsoft.Extensions.Logging;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// Telegram channel/bot adapter. Placeholder — see <see cref="PlatformNotConfiguredPublisherBase"/>.
/// A real implementation would call the official Telegram Bot API's <c>sendPhoto</c>/<c>sendMessage</c>
/// methods with the channel's bot token (resolved via <c>SocialAccount.CredentialReference</c>,
/// never stored here).
/// </summary>
public sealed class TelegramPublisher : PlatformNotConfiguredPublisherBase
{
    public override SocialPlatform Platform => SocialPlatform.Telegram;

    public TelegramPublisher(ILogger<TelegramPublisher> logger) : base(logger) { }

    public override SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: true,
        SupportsVideo: true,
        SupportsStories: false,
        SupportsHashtags: true,
        SupportsScheduling: false,
        SupportsUpdate: true,
        SupportsDelete: true,
        MaxTextLength: 100,
        MaxImages: 10,
        RequiresImage: false);
}
