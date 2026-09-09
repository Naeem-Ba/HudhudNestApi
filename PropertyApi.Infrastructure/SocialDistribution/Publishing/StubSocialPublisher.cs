using Microsoft.Extensions.Logging;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// PLACEHOLDER — no official social platform API integration exists in this codebase yet (spec
/// §11/§14/rule 13-14: never use an unofficial API, never fabricate a successful publish). One
/// instance is registered per <see cref="SocialPlatform"/> (see
/// SocialDistributionInfrastructureRegistration) so <c>PublishSocialPublicationCommandHandler</c>
/// always finds a publisher to call — every one of them deterministically returns
/// <see cref="SocialPublicationErrorCode.PlatformNotConfigured"/> (non-retryable), never a fake
/// ExternalPostId.
///
/// TO ADD A REAL PLATFORM LATER: implement <see cref="ISocialPublisher"/> against that platform's
/// official Graph/Bot API (e.g. <c>FacebookGraphApiSocialPublisher</c>), register it in
/// <c>SocialDistributionInfrastructureRegistration</c> ahead of/instead of the stub for that one
/// platform (DI resolves <see cref="ISocialPublisher"/> as <c>IEnumerable&lt;ISocialPublisher&gt;</c>
/// and <c>PublishSocialPublicationCommandHandler</c> takes the first match for the account's
/// platform) — no change to Domain, Application, or any other platform's publisher is needed.
/// </summary>
public sealed class StubSocialPublisher : ISocialPublisher
{
    private readonly ILogger<StubSocialPublisher> _logger;

    public SocialPlatform Platform { get; }

    public StubSocialPublisher(SocialPlatform platform, ILogger<StubSocialPublisher> logger)
    {
        Platform = platform;
        _logger = logger;
    }

    public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "No real ISocialPublisher is configured for platform {Platform} — publication {PublicationId} cannot be posted. This is expected until an official platform integration is added.",
            Platform,
            request.PublicationId);

        return Task.FromResult(
            SocialPublishResult.Failure(
                SocialPublicationErrorCode.PlatformNotConfigured,
                $"لا يوجد تكامل رسمي مفعّل حالياً لمنصة {Platform}."));
    }
}
