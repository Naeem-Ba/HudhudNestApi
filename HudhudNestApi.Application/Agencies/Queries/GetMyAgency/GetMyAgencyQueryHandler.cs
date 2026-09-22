using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Mapping;

namespace HudhudNestApi.Application.Agencies.Queries.GetMyAgency;

/// <summary>
/// Resolves the caller's own agency. Unlike the public page this returns an inactive
/// agency too — its owner is exactly who needs to see that it is switched off.
/// </summary>
public sealed class GetMyAgencyQueryHandler
    : IRequestHandler<GetMyAgencyQuery, AgencyDto?>
{
    private readonly IAgencyRepository _agencies;

    public GetMyAgencyQueryHandler(IAgencyRepository agencies)
    {
        _agencies = agencies;
    }

    public async Task<AgencyDto?> Handle(
        GetMyAgencyQuery request,
        CancellationToken cancellationToken)
    {
        var account = await _agencies.GetUserAccountAsync(request.RequestingUserId, cancellationToken);

        if (account?.AgencyId is null)
            return null;

        var agency = await _agencies.GetByIdAsync(account.AgencyId.Value, cancellationToken);

        if (agency is null || agency.IsDeleted)
            return null;

        var members = await _agencies.GetMembersAsync(agency.Id, cancellationToken);

        return AgencyMapper.ToDto(agency, members, members.Count);
    }
}
