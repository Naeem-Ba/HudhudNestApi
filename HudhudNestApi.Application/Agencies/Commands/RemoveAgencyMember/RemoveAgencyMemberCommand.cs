using MediatR;

namespace HudhudNestApi.Application.Agencies.Commands.RemoveAgencyMember;

/// <summary>
/// Detaches a user from an agency and removes their AgencyAgent role.
///
/// Serves both directions: an owner removing a member, and a member leaving on their own.
/// The handler decides which by comparing RequestingUserId to MemberUserId, because the
/// permission differs while the effect is identical.
/// </summary>
public sealed record RemoveAgencyMemberCommand(
    Guid AgencyId,
    Guid MemberUserId,
    Guid RequestingUserId,
    string? IpAddress) : IRequest<Unit>;
