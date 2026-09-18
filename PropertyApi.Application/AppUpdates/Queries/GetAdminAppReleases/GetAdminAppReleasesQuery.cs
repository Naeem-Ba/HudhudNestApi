using MediatR;
using PropertyApi.Application.AppUpdates.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.AppUpdates.Queries.GetAdminAppReleases;

public sealed record GetAdminAppReleasesQuery(AdminAppReleaseFilterDto Filter)
    : IRequest<PagedResult<AppReleaseAdminDto>>;
