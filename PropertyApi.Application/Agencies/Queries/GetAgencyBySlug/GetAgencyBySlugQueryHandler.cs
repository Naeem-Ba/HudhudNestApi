using MediatR;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Agencies.Mapping;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Domain.Agencies.Entities;

namespace PropertyApi.Application.Agencies.Queries.GetAgencyBySlug;

/// <summary>
/// Resolves the public agency page.
///
/// An inactive agency is reported as not found rather than as "deactivated": this endpoint
/// is anonymous, and confirming that a slug exists but is switched off tells an unauthenticated
/// caller more about the account than they need. The owner sees the real state through
/// GetMyAgency, which is authenticated.
/// </summary>
public sealed class GetAgencyBySlugQueryHandler
    : IRequestHandler<GetAgencyBySlugQuery, AgencyDto>
{
    private readonly IAgencyRepository _agencies;

    public GetAgencyBySlugQueryHandler(IAgencyRepository agencies)
    {
        _agencies = agencies;
    }

    public async Task<AgencyDto> Handle(
        GetAgencyBySlugQuery request,
        CancellationToken cancellationToken)
    {
        var slug = Agency.NormalizeSlug(request.Slug);

        var agency = await _agencies.GetBySlugAsync(slug, cancellationToken);

        if (agency is null || agency.IsDeleted || !agency.IsActive)
            throw new NotFoundException("Agency was not found.");

        var members = await _agencies.GetMembersAsync(agency.Id, cancellationToken);

        return AgencyMapper.ToDto(agency, members, members.Count);
    }
}
