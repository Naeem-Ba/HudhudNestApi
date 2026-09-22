namespace HudhudNestApi.Application.Auth.Contracts;

/// <summary>
/// Verified user information returned by Google or Apple token validation.
/// </summary>
public sealed record SocialUserInfo(
    string ProviderId,
    string ProviderName,
    string? Email,
    bool IsEmailVerified,
    string? FirstName,
    string? LastName,
    string? AvatarUrl);

public interface ISocialTokenVerifier
{
    string ProviderName { get; }

    /// <summary>
    /// <paramref name="expectedNonce"/> is the raw, unhashed nonce value the client sent to
    /// the identity provider's own SDK when starting the sign-in — not every provider's
    /// token carries one. A verifier that does not use nonces (Google, today) ignores it.
    /// A verifier that does (Apple, for B-16 in RELEASE-BLOCKERS-AR.md) must fail
    /// verification when it is missing or does not match, once the caller supplies one —
    /// otherwise a stolen identity token from an unrelated sign-in attempt could be replayed.
    /// </summary>
    Task<SocialUserInfo?> VerifyAsync(
        string token,
        string? expectedNonce = null,
        CancellationToken ct = default);
}
