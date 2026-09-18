using MediatR;
using PropertyApi.Application.AppUpdates.DTOs;
using PropertyApi.Domain.AppUpdates.Enums;

namespace PropertyApi.Application.AppUpdates.Queries.CheckAppUpdate;

public sealed record CheckAppUpdateQuery(AppPlatform Platform, string CurrentVersion, string? Language)
    : IRequest<AppUpdateCheckResultDto>;
