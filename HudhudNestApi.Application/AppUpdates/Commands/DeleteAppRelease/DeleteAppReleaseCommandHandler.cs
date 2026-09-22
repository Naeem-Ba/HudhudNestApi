using MediatR;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Application.AppUpdates.Commands.DeleteAppRelease;

/// <summary>Soft delete only — reuses BaseEntity's IsDeleted/DeletedAt convention, never a
/// physical row removal, matching every other admin-managed entity in this codebase.</summary>
public sealed class DeleteAppReleaseCommandHandler : IRequestHandler<DeleteAppReleaseCommand>
{
    private readonly IAppReleaseRepository _releases;
    private readonly IAppReleaseCacheService _cache;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _auditLog;

    public DeleteAppReleaseCommandHandler(
        IAppReleaseRepository releases, IAppReleaseCacheService cache, IUnitOfWork uow, IAuditLogService auditLog)
    {
        _releases = releases;
        _cache = cache;
        _uow = uow;
        _auditLog = auditLog;
    }

    public async Task Handle(DeleteAppReleaseCommand request, CancellationToken ct)
    {
        var release = await _releases.GetByIdAsync(request.Id, ct)
            ?? throw new NotFoundException($"App release {request.Id} was not found.");

        release.IsDeleted = true;
        release.DeletedAt = DateTime.UtcNow;
        release.DeletedByUserId = request.PerformedByUserId;

        await _uow.SaveChangesAsync(ct);

        await _cache.InvalidateAsync(release.Platform, ct);

        await _auditLog.LogAsync(
            request.PerformedByUserId,
            AuditActions.AppReleaseDeletedByAdmin,
            request.IpAddress,
            oldValue: null,
            newValue: $"{{\"id\":\"{release.Id}\",\"platform\":\"{release.Platform}\",\"version\":\"{release.Version}\"}}",
            ct);
    }
}
