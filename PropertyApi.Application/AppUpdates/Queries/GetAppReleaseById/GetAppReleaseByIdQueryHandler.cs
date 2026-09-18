using MediatR;
using PropertyApi.Application.AppUpdates.DTOs;
using PropertyApi.Application.AppUpdates.Interfaces;

namespace PropertyApi.Application.AppUpdates.Queries.GetAppReleaseById;

public sealed class GetAppReleaseByIdQueryHandler : IRequestHandler<GetAppReleaseByIdQuery, AppReleaseAdminDto?>
{
    private readonly IAppReleaseRepository _releases;

    public GetAppReleaseByIdQueryHandler(IAppReleaseRepository releases) => _releases = releases;

    public async Task<AppReleaseAdminDto?> Handle(GetAppReleaseByIdQuery request, CancellationToken ct)
    {
        var release = await _releases.GetByIdAsync(request.Id, ct);
        return release is null ? null : ToDto(release);
    }

    internal static AppReleaseAdminDto ToDto(Domain.AppUpdates.Entities.AppRelease release) => new(
        release.Id,
        release.Platform,
        release.Version,
        release.MinimumSupportedVersion,
        release.StoreUrl,
        release.ReleaseNotesAr,
        release.ReleaseNotesEn,
        release.ReleaseNotesDe,
        release.ReleaseDate,
        release.IsEnabled,
        release.CreatedAt,
        release.UpdatedAt);
}
