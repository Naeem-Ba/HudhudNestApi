using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Models;

/// <summary>
/// Everything an <see cref="Interfaces.ISocialPublisher"/> needs to attempt one publish — plain
/// data assembled by Application from <see cref="Entities.SocialAccount"/> +
/// <see cref="Entities.SocialPostContent"/>, so a publisher never touches EF/repositories
/// directly (spec §11: "عدم معرفة تفاصيل قاعدة البيانات").
/// </summary>
public sealed class SocialPublishRequest
{
    public required Guid PublicationId { get; init; }
    public required SocialPlatform Platform { get; init; }
    public required string ExternalAccountId { get; init; }

    /// <summary>Opaque reference only — never a raw token (see SocialAccount remarks).</summary>
    public string? CredentialReference { get; init; }

    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string ImageUrl { get; init; }
    public required string TargetUrl { get; init; }
    public required IReadOnlyList<string> Hashtags { get; init; }
    public required string Language { get; init; }
}
