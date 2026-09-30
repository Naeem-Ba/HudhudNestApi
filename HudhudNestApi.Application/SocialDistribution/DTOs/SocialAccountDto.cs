using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

/// <summary>
/// Deliberately has NO credential/token field at all — spec §15/§17: "عدم إرجاع Tokens أو
/// Secrets في Responses". <see cref="HasCredential"/> tells the admin UI whether a connection
/// exists without ever exposing what it is.
/// </summary>
public sealed record SocialAccountDto(
    Guid Id,
    Guid SocialChannelId,
    SocialPlatform Platform,
    string DisplayName,
    string ExternalAccountId,
    SocialAccountStatus Status,
    string AccountType,
    int? GovernorateId,
    bool HasCredential,
    DateTime? ConnectedAt,
    DateTime? DisconnectedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
