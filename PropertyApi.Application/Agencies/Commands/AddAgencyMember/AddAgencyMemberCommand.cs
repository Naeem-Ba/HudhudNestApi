using MediatR;
using PropertyApi.Application.Agencies.DTOs;

namespace PropertyApi.Application.Agencies.Commands.AddAgencyMember;

/// <summary>
/// Agency owner attaches an existing user to their agency and grants them AgencyAgent.
/// </summary>
public sealed record AddAgencyMemberCommand(
    Guid AgencyId,
    Guid MemberUserId,
    Guid RequestingUserId,
    string? IpAddress) : IRequest<AgencyMemberDto>;
