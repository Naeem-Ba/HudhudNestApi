using MediatR;
using PropertyApi.Application.Agencies.DTOs;

namespace PropertyApi.Application.Agencies.Commands.TransferAgencyOwnership;

/// <summary>
/// Owner-only transfer of Agency.OwnerUserId to another user — the missing Application
/// layer around Agency.TransferOwnership(), which was written and unit-tested on the
/// domain but had zero production callers (B-4b).
///
/// NewOwnerUserId must already be a member of THIS agency (UserAccount.AgencyId ==
/// AgencyId). Membership can only be gained through the invitation/accept consent flow
/// (B-2, RELEASE-BLOCKERS-AR.md) — letting a transfer hand ownership to someone who never
/// consented to join the agency at all would be a bigger version of the exact problem B-2
/// closed, so this command does not auto-join the target the way it would need to for a
/// non-member to qualify.
/// </summary>
public sealed record TransferAgencyOwnershipCommand(
    Guid AgencyId,
    Guid NewOwnerUserId,
    Guid RequestingUserId,
    string? IpAddress) : IRequest<AgencyDto>;
