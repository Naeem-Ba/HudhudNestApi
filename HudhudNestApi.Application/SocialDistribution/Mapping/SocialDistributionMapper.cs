using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Mapping;

public static class SocialDistributionMapper
{
    public static SocialChannelDto ToDto(SocialChannel channel) => new(
        channel.Id,
        channel.Platform,
        channel.Name,
        channel.Status,
        channel.ConfigurationVersion,
        channel.CreatedAt,
        channel.UpdatedAt);

    public static SocialAccountDto ToDto(SocialAccount account) => new(
        account.Id,
        account.SocialChannelId,
        account.Platform,
        account.DisplayName,
        account.ExternalAccountId,
        account.Status,
        account.AccountType.ToString(),
        account.GovernorateId,
        HasCredential: !string.IsNullOrEmpty(account.CredentialReference),
        account.ConnectedAt,
        account.DisconnectedAt,
        account.CreatedAt,
        account.UpdatedAt);

    public static SocialPostContentDto ToDto(SocialPostContent content) => new(
        content.Id,
        content.Title,
        content.Body,
        content.ImageUrl,
        content.TargetUrl,
        content.HashtagList,
        content.Language,
        content.ContentVersion,
        content.ReviewStatus,
        content.ReviewNote);

    public static SocialPublicationDto ToDto(SocialPublication publication) => new(
        publication.Id,
        publication.PropertyId,
        publication.SocialAccountId,
        publication.Status,
        publication.ScheduledAt,
        publication.StartedAt,
        publication.PublishedAt,
        publication.FailedAt,
        publication.CancelledAt,
        publication.ExternalPostId,
        publication.ExternalPostUrl,
        publication.ErrorCode,
        publication.ErrorMessage,
        publication.RetryCount,
        publication.MaxRetryCount,
        publication.LastRetryAt,
        publication.NextRetryAt,
        publication.UtmSource,
        publication.UtmMedium,
        publication.UtmCampaign,
        publication.UtmContent,
        publication.Content is null ? null : ToDto(publication.Content),
        publication.CreatedAt,
        publication.UpdatedAt,
        publication.DistributionRuleId,
        publication.DistributionRunId);

    public static SocialPublicationDeadLetterDto ToDto(SocialPublicationDeadLetter deadLetter) => new(
        deadLetter.Id,
        deadLetter.PublicationId,
        deadLetter.SocialAccountId,
        deadLetter.Platform,
        deadLetter.LastErrorCode,
        deadLetter.LastErrorMessage,
        deadLetter.Attempts,
        deadLetter.FailedAt,
        deadLetter.ResolvedAt,
        deadLetter.ResolvedByUserId,
        deadLetter.ResolutionNote);

    public static SocialPublicationStatusHistoryDto ToDto(SocialPublicationStatusHistory history) => new(
        history.Id,
        history.FromStatus,
        history.ToStatus,
        history.ChangedByUserId,
        history.Note,
        history.CreatedAt);
}
