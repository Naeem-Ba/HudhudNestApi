using MediatR;
using PropertyApi.Application.AppUpdates.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.AppUpdates.Commands.EnableAppRelease;

public sealed class EnableAppReleaseCommandHandler : IRequestHandler<EnableAppReleaseCommand>
{
    private readonly IAppReleaseRepository _releases;
    private readonly IAppReleaseCacheService _cache;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public EnableAppReleaseCommandHandler(
        IAppReleaseRepository releases, IAppReleaseCacheService cache, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _releases = releases;
        _cache = cache;
        _uow = uow;
        _auditLog = auditLog;
    }

    public async Task Handle(EnableAppReleaseCommand request, CancellationToken ct)
    {
        var release = await _releases.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"App release {request.Id} was not found.");

        if (await _releases.ExistsEnabledForPlatformAndVersionAsync(release.Platform, release.Version, request.Id, ct))
        {
            throw new ConflictException(
                $"An enabled release already exists for {release.Platform} version {release.Version}.");
        }

        release.Enable();
        await _uow.SaveChangesAsync(ct);

        await _cache.InvalidateAsync(release.Platform, ct);

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.AppReleaseEnabledByAdmin,
            request.IpAddress,
            oldValue: null,
            newValue: $"{{\"id\":\"{release.Id}\",\"platform\":\"{release.Platform}\",\"version\":\"{release.Version}\"}}",
            ct);
    }
}
