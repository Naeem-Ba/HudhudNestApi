namespace PropertyApi.Application.Auth.Contracts;

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

    Task<SocialUserInfo?> VerifyAsync(string token, CancellationToken ct = default);
}
