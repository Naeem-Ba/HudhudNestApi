using MediatR;
using PropertyApi.Application.Agencies.DTOs;

namespace PropertyApi.Application.Agencies.Commands.UpdateAgency;

/// <summary>
/// Owner-only update of the agency's editable profile fields — exactly the fields
/// Agency.UpdateProfile has always accepted. RELEASE-BLOCKERS-AR.md B-4: the domain method
/// was already complete; this is the thin command layer the report says was missing.
/// </summary>
public sealed record UpdateAgencyCommand(
    Guid AgencyId,
    string Name,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? City,
    Guid RequestingUserId) : IRequest<AgencyDto>;
