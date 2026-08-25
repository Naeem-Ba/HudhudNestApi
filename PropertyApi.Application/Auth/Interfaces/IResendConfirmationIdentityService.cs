using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// What resending a confirmation link needs from Identity, and nothing more.
///
/// Both members are shaped identically to ones already on IAddEmailIdentityService, and
/// deliberately so -- for the same reason IRegisterIdentityService restates the token
/// member. Resending a link to an address the account already holds is not adding an
/// email, so the handler should not have to reach for the add-email contract to do it.
/// One class behind IIdentityCapabilityAdapter satisfies all of them with one method each.
/// </summary>
public interface IResendConfirmationIdentityService
{
    Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<string> GenerateEmailConfirmationTokenAsync(
        Guid identityId,
        CancellationToken ct = default);
}
