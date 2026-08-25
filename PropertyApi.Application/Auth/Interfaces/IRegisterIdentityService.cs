using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the email registration workflow.
/// </summary>
public interface IRegisterIdentityService
{
    Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<IdentityOperationResult> CreateAsync(
        CreateIdentityAccount request,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddToRoleAsync(
        Guid identityId,
        string role,
        CancellationToken ct = default);

    /// <summary>
    /// Mints the address-confirmation token for a freshly created account.
    ///
    /// Identical in shape to <see cref="IAddEmailIdentityService" />'s member, and
    /// deliberately so: both workflows need the same Identity capability, and the
    /// single class behind <c>IIdentityCapabilityAdapter</c> satisfies both with one
    /// method. Declaring it here keeps this interface an honest statement of what
    /// registration actually requires instead of making the handler reach for the
    /// add-email contract to do something that is not adding an email.
    /// </summary>
    Task<string> GenerateEmailConfirmationTokenAsync(
        Guid identityId,
        CancellationToken ct = default);
}
