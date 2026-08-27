using PropertyApi.Domain.Agencies.Enums;

namespace PropertyApi.Application.Agencies.DTOs;

/// <summary>
/// An agency invitation as shown to its target user (their invitations inbox) and
/// returned to the owner who just sent it. Deliberately carries no more of the agency
/// than its name/slug — the full AgencyDto is a separate, authenticated-only fetch.
/// </summary>
public sealed record AgencyInvitationDto(
    Guid Id,
    Guid AgencyId,
    string AgencyName,
    string AgencySlug,
    Guid InviterUserId,
    string InviterName,
    Guid TargetUserId,
    AgencyInvitationStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RespondedAt);
