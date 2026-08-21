using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Auth.Tests.TestHelpers;

internal sealed class SocialSessionIdentityAdapter : ILoginIdentityService
{
    private readonly ISocialLoginIdentityService _inner;
    public SocialSessionIdentityAdapter(ISocialLoginIdentityService inner) => _inner = inner;
    public Task<IReadOnlyList<string>> GetRolesAsync(Guid id, CancellationToken ct = default) =>
        _inner.GetRolesAsync(id, ct);
    public Task<IdentityOperationResult> RecordSuccessfulLoginAsync(Guid id, DateTime at, CancellationToken ct = default) =>
        _inner.RecordSuccessfulLoginAsync(id, at, ct);
    public Task<IdentityAccountSnapshot?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        throw new NotSupportedException();
#pragma warning disable CS0618
    public Task<bool> CheckPasswordAsync(Guid id, string password, CancellationToken ct = default) =>
        throw new NotSupportedException();
#pragma warning restore CS0618
    public Task<LoginPasswordVerificationResult> VerifyPasswordWithLockoutAsync(Guid id, string password, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<bool> IsLockedOutAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<int> GetAccessFailedCountAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<DateTimeOffset?> GetLockoutEndAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

internal sealed class PhoneSessionIdentityAdapter : ILoginIdentityService
{
    private readonly IPhoneOtpIdentityService _inner;
    public PhoneSessionIdentityAdapter(IPhoneOtpIdentityService inner) => _inner = inner;
    public Task<IReadOnlyList<string>> GetRolesAsync(Guid id, CancellationToken ct = default) =>
        _inner.GetRolesAsync(id, ct);
    public Task<IdentityOperationResult> RecordSuccessfulLoginAsync(Guid id, DateTime at, CancellationToken ct = default) =>
        Task.FromResult(IdentityOperationResult.Success());
    public Task<IdentityAccountSnapshot?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        throw new NotSupportedException();
#pragma warning disable CS0618
    public Task<bool> CheckPasswordAsync(Guid id, string password, CancellationToken ct = default) =>
        throw new NotSupportedException();
#pragma warning restore CS0618
    public Task<LoginPasswordVerificationResult> VerifyPasswordWithLockoutAsync(Guid id, string password, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<bool> IsLockedOutAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<int> GetAccessFailedCountAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<DateTimeOffset?> GetLockoutEndAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

internal sealed class RefreshTokenRepositoryAdapter : IRefreshTokenRepository
{
    private readonly IRefreshTokenStore _inner;
    public RefreshTokenRepositoryAdapter(IRefreshTokenStore inner) => _inner = inner;
    public Task AddAsync(Guid userId, string token, string? ip, CancellationToken ct = default) =>
        _inner.StoreAsync(userId, token, ip, ct);
    public Task<RefreshTokenRecord?> GetByRefreshTokenAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> RevokeIfActiveAsync(Guid id, DateTime now, string? ip, string? replacement, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RevokeActiveTokensForUserAsync(Guid id, DateTime now, string? ip, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> RevokeUserTokenAsync(Guid id, string token, DateTime now, string? ip, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default) => action(ct);
}
