using MediatR;
using PropertyApi.Application.Agencies.DTOs;

namespace PropertyApi.Application.Agencies.Queries.GetAgencyBySlug;

/// <summary>Public agency page lookup, by slug.</summary>
public sealed record GetAgencyBySlugQuery(string Slug) : IRequest<AgencyDto>;
