using System.Text.Json;
using MediatR;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.AppUpdates.Entities;
using HudhudNestApi.Domain.AppUpdates.ValueObjects;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Application.AppUpdates.Commands.CreateAppRelease;

public sealed class CreateAppReleaseCommandHandler : IRequestHandler<CreateAppReleaseCommand, Guid>
{
    private readonly IAppReleaseRepository _releases;
    private readonly IAppReleaseCacheService _cache;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public CreateAppReleaseCommandHandler(
        IAppReleaseRepository releases, IAppReleaseCacheService cache, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _releases = releases;
        _cache = cache;
        _uow = uow;
        _auditLog = auditLog;
    }

    public async Task<Guid> Handle(CreateAppReleaseCommand request, CancellationToken ct)
    {
        var version = AppVersion.Parse(request.Version);
        var minimumSupportedVersion = AppVersion.Parse(request.MinimumSupportedVersion);

        if (request.IsEnabled &&
            await _releases.ExistsEnabledForPlatformAndVersionAsync(request.Platform, version.ToString(), excludeId: null, ct))
        {
            throw new ConflictException(
                $"An enabled release already exists for {request.Platform} version {version}.");
        }

        var release = AppRelease.Create(
            request.Platform, version, minimumSupportedVersion, request.StoreUrl,
            request.ReleaseNotesAr, request.ReleaseNotesEn, request.ReleaseNotesDe,
            request.ReleaseDate, request.IsEnabled);

        _releases.Add(release);
        await _uow.SaveChangesAsync(ct);

        await _cache.InvalidateAsync(request.Platform, ct);

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.AppReleaseCreatedByAdmin,
            request.IpAddress,
            oldValue: null,
            newValue: JsonSerializer.Serialize(Snapshot(release)),
            ct);

        return release.Id;
    }

    internal static object Snapshot(AppRelease release) => new
    {
        release.Id,
        Platform = release.Platform.ToString(),
        release.Version,
        release.MinimumSupportedVersion,
        release.StoreUrl,
        release.ReleaseDate,
        release.IsEnabled,
    };
}
