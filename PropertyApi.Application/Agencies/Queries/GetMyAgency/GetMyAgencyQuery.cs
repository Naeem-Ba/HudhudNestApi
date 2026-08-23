using MediatR;
using PropertyApi.Application.Agencies.DTOs;

namespace PropertyApi.Application.Agencies.Queries.GetMyAgency;

/// <summary>
/// The agency the caller belongs to, owner or member.
/// Returns null when the caller is independent — that is the normal case, not an error.
/// </summary>
public sealed record GetMyAgencyQuery(Guid RequestingUserId) : IRequest<AgencyDto?>;
