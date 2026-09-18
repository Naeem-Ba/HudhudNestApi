using PropertyApi.Domain.AppUpdates.Enums;

namespace PropertyApi.Application.AppUpdates.DTOs;

public sealed record AdminAppReleaseFilterDto
{
    public AppPlatform? Platform { get; init; }
    public bool? IsEnabled { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
