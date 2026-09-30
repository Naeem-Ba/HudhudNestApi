using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Agencies.Mapping;

/// <summary>
/// Agency → DTO projection, in one place so the public page, the owner dashboard and the
/// create response cannot drift into showing different fields for the same entity.
/// </summary>
public static class AgencyMapper
{
    public static AgencyDto ToDto(
        Agency agency,
        IReadOnlyList<UserAccount> members,
        int memberCount)
        => new(
            Id: agency.Id,
            Name: agency.Name,
            Slug: agency.Slug,
            Description: agency.Description,
            LogoUrl: agency.LogoUrl,
            ContactEmail: agency.ContactEmail,
            ContactPhone: agency.ContactPhone,
            City: agency.City,
            CountryCode: agency.CountryCode,
            LicenseNumber: agency.LicenseNumber,
            OwnerUserId: agency.OwnerUserId,
            IsActive: agency.IsActive,
            MemberCount: memberCount,
            CreatedAt: agency.CreatedAt,
            Members: members
                .Select(member => ToMemberDto(member, agency.OwnerUserId))
                .ToList(),
            GovernorateId: agency.GovernorateId,
            DistrictId: agency.DistrictId,
            NeighborhoodId: agency.NeighborhoodId);

    public static AgencyMemberDto ToMemberDto(UserAccount member, Guid ownerUserId)
        => new(
            UserId: member.Id,
            // DisplayName is optional on UserAccount; fall back to the real name rather
            // than rendering an empty card on the agency's public page.
            DisplayName: string.IsNullOrWhiteSpace(member.DisplayName)
                ? $"{member.FirstName} {member.LastName}".Trim()
                : member.DisplayName!,
            ProfileImageUrl: member.ProfileImageUrl,
            IsOwner: member.Id == ownerUserId,
            JoinedAt: member.AgencyJoinedAt);

    public static AgencyInvitationDto ToInvitationDto(
        AgencyInvitation invitation,
        Agency agency,
        UserAccount inviter)
        => new(
            Id: invitation.Id,
            AgencyId: agency.Id,
            AgencyName: agency.Name,
            AgencySlug: agency.Slug,
            InviterUserId: inviter.Id,
            InviterName: string.IsNullOrWhiteSpace(inviter.DisplayName)
                ? $"{inviter.FirstName} {inviter.LastName}".Trim()
                : inviter.DisplayName!,
            TargetUserId: invitation.TargetUserId,
            Status: invitation.Status,
            CreatedAt: invitation.CreatedAt,
            ExpiresAt: invitation.ExpiresAt,
            RespondedAt: invitation.RespondedAt);
}
