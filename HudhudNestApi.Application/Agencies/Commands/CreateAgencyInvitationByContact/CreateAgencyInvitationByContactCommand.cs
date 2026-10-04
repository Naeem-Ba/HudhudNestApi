using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitationByContact;

/// <summary>
/// Owner invites a user identified by the e-mail address or phone number they signed up
/// with, instead of by user id. Exactly one of Email / PhoneNumber is set (see the
/// validator). Resolves the account and then hands over to CreateAgencyInvitationCommand,
/// so the consent model, the advisory lock and every membership rule stay in one place.
///
/// Not found is reported as such (404), not hidden behind a uniform answer: registration
/// already answers "Email already registered" for a taken address, so a uniform response
/// here would hide nothing. The "agency-invite" rate limit is what bounds probing.
/// </summary>
public sealed record CreateAgencyInvitationByContactCommand(
    Guid AgencyId,
    string? Email,
    string? PhoneNumber,
    Guid RequestingUserId,
    string? IpAddress) : IRequest<AgencyInvitationDto>;
