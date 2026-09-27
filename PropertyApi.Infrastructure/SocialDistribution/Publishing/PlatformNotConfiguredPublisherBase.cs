using Microsoft.Extensions.Logging;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Domain.SocialDistribution.Policies;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// PLACEHOLDER base class — no official social platform API integration exists in this codebase
/// yet (spec rule 9-10: never use an unofficial API, never fabricate a successful publish). Every
/// concrete subclass in this folder (<see cref="FacebookPublisher"/>, etc.) is a distinct,
/// independently-registrable <see cref="ISocialPublisher"/> adapter — a REAL Ports-and-Adapters
/// seam, not one generic class parameterized by an enum — but until a real API integration is
/// authorized, all six share this identical, safe fallback behavior: local content validation
/// against the platform's real, documented <see cref="SocialContentPolicy"/> limits and
/// <see cref="Capabilities"/> (so validation failures are genuine, not part of the placeholder),
/// but <see cref="PublishAsync"/> always deterministically returns
/// <see cref="SocialPublicationErrorCode.PlatformNotConfigured"/> (non-retryable) — never a fake
/// ExternalPostId.
///
/// TO ADD A REAL PLATFORM LATER: create e.g. <c>FacebookGraphApiSocialPublisher : ISocialPublisher</c>
/// (NOT a subclass of this base — a real adapter has nothing in common with a placeholder) against
/// that platform's official Graph/Bot API, and register it in
/// <c>SocialDistributionInfrastructureRegistration</c> ahead of/instead of <see cref="FacebookPublisher"/>.
/// <see cref="SocialPublisherRegistry"/> resolves by platform, last registration wins — no change
/// to Domain, Application, the Distribution Engine, the Queue, the Worker, or any other platform's
/// publisher is needed.
/// </summary>
public abstract class PlatformNotConfiguredPublisherBase : ISocialPublisher
{
    private readonly ILogger _logger;

    public abstract SocialPlatform Platform { get; }

    protected PlatformNotConfiguredPublisherBase(ILogger logger) => _logger = logger;

    public abstract SocialPublisherCapabilities GetCapabilities();

    public virtual SocialContentValidationResult ValidateContent(SocialPublishRequest request) =>
        SocialContentValidator.Validate(request, Platform, GetCapabilities());

    public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "No real ISocialPublisher is configured for platform {Platform} — publication {PublicationId} cannot be posted. This is expected until an official platform integration is added.",
            Platform,
            request.PublicationId);

        return Task.FromResult(NotConfiguredFailure());
    }

    /// <summary>Placeholder (Phase 11 spec §7) — same "never fabricate success" contract as <see cref="PublishAsync"/>. A real adapter would call the platform's real edit endpoint here.</summary>
    public Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "No real ISocialPublisher is configured for platform {Platform} — cannot update external post {ExternalPostId}.",
            Platform, externalPostId);

        return Task.FromResult(NotConfiguredFailure());
    }

    /// <summary>Placeholder (Phase 11 spec §7).</summary>
    public Task<SocialPublishResult> CommentAsync(SocialPublishRequest request, string externalPostId, string commentBody, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "No real ISocialPublisher is configured for platform {Platform} — cannot comment on external post {ExternalPostId}.",
            Platform, externalPostId);

        return Task.FromResult(NotConfiguredFailure());
    }

    /// <summary>Placeholder (Phase 11 spec §7).</summary>
    public Task<SocialPublishResult> DeleteAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "No real ISocialPublisher is configured for platform {Platform} — cannot delete external post {ExternalPostId}.",
            Platform, externalPostId);

        return Task.FromResult(NotConfiguredFailure());
    }

    private SocialPublishResult NotConfiguredFailure() =>
        SocialPublishResult.Failure(
            SocialPublicationErrorCode.PlatformNotConfigured,
            $"لا يوجد تكامل رسمي مفعّل حالياً لمنصة {Platform}.");
}
