using MediatR;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.AppUpdates.Commands.CreateAppRelease;

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
