using MediatR;
using PropertyApi.Application.AppUpdates.DTOs;

namespace PropertyApi.Application.AppUpdates.Queries.GetAppReleaseById;

public sealed record GetAppReleaseByIdQuery(Guid Id) : IRequest<AppReleaseAdminDto?>;
