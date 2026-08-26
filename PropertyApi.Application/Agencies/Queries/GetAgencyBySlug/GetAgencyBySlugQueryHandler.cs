using MediatR;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Agencies.Mapping;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Common.Exceptions;

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
        Agency? agency;

        try
        {
            // RELEASE-BLOCKERS-AR.md B-12: NormalizeSlug throws DomainException for a
            // malformed slug (e.g. "---"), which the exception middleware maps to 400 — while
            // a well-formed slug that simply does not exist answers 404 below. That
            // difference is itself a signal to an unauthenticated caller, and contradicts
            // this handler's own stated intent of revealing nothing about a slug's validity.
            // A malformed slug IS an unknown slug; both answer the same way now.
            var slug = Agency.NormalizeSlug(request.Slug);
            agency = await _agencies.GetBySlugAsync(slug, cancellationToken);
        }
        catch (DomainException)
        {
            throw new NotFoundException("Agency was not found.");
        }

        if (agency is null || agency.IsDeleted || !agency.IsActive)
            throw new NotFoundException("Agency was not found.");

        var members = await _agencies.GetMembersAsync(agency.Id, cancellationToken);

        // Interim mitigation for B-2 (RELEASE-BLOCKERS-AR.md): AddAgencyMemberCommandHandler
        // lets an owner attach any user id it knows with no consent step at all — no
        // invitation, no acceptance, nothing. That gap is not closed here (fixing it for real
        // needs an invitation entity and an accept/decline flow, deliberately deferred as a
        // product decision). What this closes is the part that is otherwise irreversible the
        // moment it happens: publishing a real person's name and photo on this anonymous page
        // before they ever agreed to be associated with the agency. The owner's own consent is
        // implicit in owning the agency, so only the owner is shown here. Every other member is
        // still a real member operationally — GetMyAgencyQueryHandler still shows the owner
        // their full roster — they are just not published publicly yet.
        var publicMembers = members
            .Where(member => member.Id == agency.OwnerUserId)
            .ToList();

        return AgencyMapper.ToDto(agency, publicMembers, members.Count);
    }
}
