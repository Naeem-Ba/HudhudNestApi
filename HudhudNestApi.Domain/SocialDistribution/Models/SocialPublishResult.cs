using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Models;

/// <summary>
/// The outcome of one <see cref="Interfaces.ISocialPublisher"/> publish attempt. Plain data —
/// no framework/HTTP types — so it can flow from Infrastructure (where the actual platform call
/// happens) back through Application into <see cref="Entities.SocialPublication"/>'s state
/// machine without either layer depending on the other's concerns.
/// </summary>
public sealed class SocialPublishResult
{
    public bool IsSuccess { get; }

    /// <summary>Set only when <see cref="IsSuccess"/> is true.</summary>
    public string? ExternalPostId { get; }

    /// <summary>Set only when <see cref="IsSuccess"/> is true and the platform exposes a public URL for the post.</summary>
    public string? ExternalPostUrl { get; }

    /// <summary>Set only when <see cref="IsSuccess"/> is false.</summary>
    public SocialPublicationErrorCode? ErrorCode { get; }

    /// <summary>
    /// Developer-facing, safe-to-store failure detail. Never the raw exception message from an
    /// HTTP client (which can carry request headers/tokens) — publishers are responsible for
    /// reducing whatever they caught down to a short, sanitized sentence before returning it.
    /// </summary>
    public string? ErrorMessage { get; }

    private SocialPublishResult(
        bool isSuccess,
        string? externalPostId,
        string? externalPostUrl,
        SocialPublicationErrorCode? errorCode,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        ExternalPostId = externalPostId;
        ExternalPostUrl = externalPostUrl;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static SocialPublishResult Success(string externalPostId, string? externalPostUrl = null) =>
        new(true, externalPostId, externalPostUrl, null, null);

    public static SocialPublishResult Failure(SocialPublicationErrorCode errorCode, string errorMessage) =>
        new(false, null, null, errorCode, errorMessage);
}
