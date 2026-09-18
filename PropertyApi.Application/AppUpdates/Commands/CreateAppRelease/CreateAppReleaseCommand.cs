using MediatR;
using PropertyApi.Domain.AppUpdates.Enums;

namespace PropertyApi.Application.AppUpdates.Commands.CreateAppRelease;

public sealed record CreateAppReleaseCommand(
    AppPlatform Platform,
    string Version,
    string MinimumSupportedVersion,
    string? StoreUrl,
    string? ReleaseNotesAr,
    string? ReleaseNotesEn,
    string? ReleaseNotesDe,
    DateTime ReleaseDate,
    bool IsEnabled,
    Guid PerformedByUserId,
    string? IpAddress) : IRequest<Guid>;
