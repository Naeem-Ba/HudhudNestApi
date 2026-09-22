using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Queries.GetAgencyBySlug;

/// <summary>Public agency page lookup, by slug.</summary>
public sealed record GetAgencyBySlugQuery(string Slug) : IRequest<AgencyDto>;
