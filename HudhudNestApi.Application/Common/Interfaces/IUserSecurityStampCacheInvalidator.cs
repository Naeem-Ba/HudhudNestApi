namespace HudhudNestApi.Application.Common.Interfaces;

/// <summary>
/// Invalidates cached security-stamp snapshots after identity-sensitive changes.
/// The implementation must use the same distributed cache key format as the
/// security-stamp validator so that all API instances observe the change.
/// </summary>
public interface IUserSecurityStampCacheInvalidator
{
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
