using MediatR;
using PropertyApi.Application.AppUpdates.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.AppUpdates.Commands.DisableAppRelease;

public sealed class DisableAppReleaseCommandHandler : IRequestHandler<DisableAppReleaseCommand>
{
    private readonly IAppReleaseRepository _releases;
    private readonly IAppReleaseCacheService _cache;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public DisableAppReleaseCommandHandler(
        IAppReleaseRepository releases, IAppReleaseCacheService cache, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _releases = releases;
        _cache = cache;
        _uow = uow;
        _auditLog = auditLog;
    }

    public async Task Handle(DisableAppReleaseCommand request, CancellationToken ct)
    {
        var release = await _releases.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"App release {request.Id} was not found.");

        release.Disable();
        await _uow.SaveChangesAsync(ct);

        await _cache.InvalidateAsync(release.Platform, ct);

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.AppReleaseDisabledByAdmin,
            request.IpAddress,
            oldValue: null,
            newValue: $"{{\"id\":\"{release.Id}\",\"platform\":\"{release.Platform}\",\"version\":\"{release.Version}\"}}",
            ct);
    }
}
