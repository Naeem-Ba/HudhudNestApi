using MediatR;
using HudhudNestApi.Application.AppUpdates.DTOs;

namespace HudhudNestApi.Application.AppUpdates.Queries.GetAppReleaseById;

public sealed record GetAppReleaseByIdQuery(Guid Id) : IRequest<AppReleaseAdminDto?>;
