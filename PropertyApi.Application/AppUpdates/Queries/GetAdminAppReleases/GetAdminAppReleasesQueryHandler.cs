using MediatR;
using PropertyApi.Application.AppUpdates.DTOs;
using PropertyApi.Application.AppUpdates.Interfaces;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.AppUpdates.Queries.GetAdminAppReleases;

public sealed class GetAdminAppReleasesQueryHandler
    : IRequestHandler<GetAdminAppReleasesQuery, PagedResult<AppReleaseAdminDto>>
{
    private readonly IAppReleaseRepository _releases;

    public GetAdminAppReleasesQueryHandler(IAppReleaseRepository releases) => _releases = releases;

    public Task<PagedResult<AppReleaseAdminDto>> Handle(GetAdminAppReleasesQuery request, CancellationToken ct) =>
        _releases.GetAdminListAsync(request.Filter, ct);
}
