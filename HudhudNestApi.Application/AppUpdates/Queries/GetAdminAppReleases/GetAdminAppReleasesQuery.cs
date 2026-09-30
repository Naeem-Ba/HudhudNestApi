using MediatR;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.AppUpdates.Queries.GetAdminAppReleases;

public sealed record GetAdminAppReleasesQuery(AdminAppReleaseFilterDto Filter)
    : IRequest<PagedResult<AppReleaseAdminDto>>;
