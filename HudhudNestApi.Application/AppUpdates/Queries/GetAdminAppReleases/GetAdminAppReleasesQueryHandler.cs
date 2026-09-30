using MediatR;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.AppUpdates.Queries.GetAdminAppReleases;

public sealed class GetAdminAppReleasesQueryHandler
    : IRequestHandler<GetAdminAppReleasesQuery, PagedResult<AppReleaseAdminDto>>
{
    private readonly IAppReleaseRepository _releases;

    public GetAdminAppReleasesQueryHandler(IAppReleaseRepository releases) => _releases = releases;

    public Task<PagedResult<AppReleaseAdminDto>> Handle(GetAdminAppReleasesQuery request, CancellationToken ct) =>
        _releases.GetAdminListAsync(request.Filter, ct);
}
