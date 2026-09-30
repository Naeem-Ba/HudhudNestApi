using MediatR;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.AppUpdates.Queries.CheckAppUpdate;

public sealed record CheckAppUpdateQuery(AppPlatform Platform, string CurrentVersion, string? Language)
    : IRequest<AppUpdateCheckResultDto>;
