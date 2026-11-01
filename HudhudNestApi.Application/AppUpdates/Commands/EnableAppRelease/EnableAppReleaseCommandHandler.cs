using MediatR;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.AppUpdates.Policies;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Application.AppUpdates.Commands.EnableAppRelease;

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

        // Already on: nothing changed, so nothing is written to the audit log (phase 14, D09).
        if (release.IsEnabled)
            return;

        // Re-checked on enable: a release may have been saved while disabled without its store link.
        var linkProblem = AppStoreLinkPolicy.Check(release.Platform, release.StoreUrl, enabled: true);
        if (linkProblem is not null)
            throw new ValidationException("StoreUrl", linkProblem);

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
