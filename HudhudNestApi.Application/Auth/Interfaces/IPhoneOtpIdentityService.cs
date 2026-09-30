using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the phone OTP verification workflow.
/// </summary>
public interface IPhoneOtpIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityAccountSnapshot?> FindByPhoneNumberAsync(
        string phoneNumber,
        CancellationToken ct = default);

    Task<IdentityOperationResult> CreateAsync(
        CreateIdentityAccount request,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddToRoleAsync(
        Guid identityId,
        string role,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ConfirmPhoneNumberAsync(
        Guid identityId,
        DateTime confirmedAtUtc,
        CancellationToken ct = default);
}
