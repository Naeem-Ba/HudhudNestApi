namespace HudhudNestApi.Application.Agencies.DTOs;

/// <summary>
/// A pending invitation as the inviting agency's owner sees it: who it went to and when it
/// lapses. Name and avatar only, same thinness as AgencyMemberDto; the target's contact
/// details stay on their own profile.
/// </summary>
public sealed record AgencySentInvitationDto(
    Guid Id,
    Guid TargetUserId,
    string TargetName,
    string? TargetProfileImageUrl,
    DateTime CreatedAt,
    DateTime ExpiresAt);
