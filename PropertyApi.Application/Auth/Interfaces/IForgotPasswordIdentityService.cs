using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the forgot-password workflow.
/// </summary>
public interface IForgotPasswordIdentityService
{
    Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<string> GeneratePasswordResetTokenAsync(
        Guid identityId,
        CancellationToken ct = default);
}
