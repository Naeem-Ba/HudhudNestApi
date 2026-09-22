using MediatR;

namespace HudhudNestApi.Application.AppUpdates.Commands.UpdateAppRelease;

/// <summary>Platform is deliberately not editable — changing platform is really "create a new
/// row for a different platform," not an edit of this one.</summary>
public sealed record UpdateAppReleaseCommand(
    Guid Id,
    string Version,
    string MinimumSupportedVersion,
    string? StoreUrl,
    string? ReleaseNotesAr,
    string? ReleaseNotesEn,
    string? ReleaseNotesDe,
    DateTime ReleaseDate,
    Guid PerformedByUserId,
    string? IpAddress) : IRequest;
