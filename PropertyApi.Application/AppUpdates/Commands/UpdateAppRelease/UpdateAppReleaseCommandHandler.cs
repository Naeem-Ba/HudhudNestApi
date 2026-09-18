using System.Text.Json;
using MediatR;
using PropertyApi.Application.AppUpdates.Commands.CreateAppRelease;
using PropertyApi.Application.AppUpdates.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.AppUpdates.ValueObjects;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.AppUpdates.Commands.UpdateAppRelease;

public sealed class UpdateAppReleaseCommandHandler : IRequestHandler<UpdateAppReleaseCommand>
{
    private readonly IAppReleaseRepository _releases;
    private readonly IAppReleaseCacheService _cache;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public UpdateAppReleaseCommandHandler(
        IAppReleaseRepository releases, IAppReleaseCacheService cache, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _releases = releases;
        _cache = cache;
        _uow = uow;
        _auditLog = auditLog;
    }

    public async Task Handle(UpdateAppReleaseCommand request, CancellationToken ct)
    {
        var release = await _releases.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"App release {request.Id} was not found.");

        var oldSnapshot = CreateAppReleaseCommandHandler.Snapshot(release);

        var version = AppVersion.Parse(request.Version);
        var minimumSupportedVersion = AppVersion.Parse(request.MinimumSupportedVersion);

        if (release.IsEnabled &&
            await _releases.ExistsEnabledForPlatformAndVersionAsync(release.Platform, version.ToString(), request.Id, ct))
        {
            throw new ConflictException(
                $"An enabled release already exists for {release.Platform} version {version}.");
        }

        release.UpdateContent(
            version, minimumSupportedVersion, request.StoreUrl,
            request.ReleaseNotesAr, request.ReleaseNotesEn, request.ReleaseNotesDe, request.ReleaseDate);

        await _uow.SaveChangesAsync(ct);

        await _cache.InvalidateAsync(release.Platform, ct);

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.AppReleaseUpdatedByAdmin,
            request.IpAddress,
            oldValue: JsonSerializer.Serialize(oldSnapshot),
            newValue: JsonSerializer.Serialize(CreateAppReleaseCommandHandler.Snapshot(release)),
            ct);
    }
}
