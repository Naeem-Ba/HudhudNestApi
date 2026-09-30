using HudhudNestApi.Application.Auth.Contracts;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Integration.Tests.Auth;

internal sealed record SocialIdentitySeed(
    string RawToken,
    string ProviderName,
    string ProviderId,
    string? Email,
    bool IsEmailVerified,
    string? FirstName = "Social",
    string? LastName = "User",
    string? AvatarUrl = null);

internal sealed class TestSocialTokenVerifier : ISocialTokenVerifier
{
    private readonly string _providerName;
    private readonly IReadOnlyDictionary<string, SocialIdentitySeed> _seeds;

    public TestSocialTokenVerifier(
        string providerName,
        IEnumerable<SocialIdentitySeed> seeds)
    {
        _providerName = providerName;
        _seeds = seeds.ToDictionary(x => x.RawToken, StringComparer.Ordinal);
    }

    public string ProviderName => _providerName;

    public Task<SocialUserInfo?> VerifyAsync(
        string token,
        string? expectedNonce = null,
        CancellationToken ct = default)
    {
        if (!_seeds.TryGetValue(token, out var seed))
        {
            return Task.FromResult<SocialUserInfo?>(null);
        }

        var socialUser = AuthTestReflection.Create<SocialUserInfo>(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProviderName"] = seed.ProviderName,
                ["ProviderId"] = seed.ProviderId,
                ["Email"] = seed.Email,
                ["IsEmailVerified"] = seed.IsEmailVerified,
                ["FirstName"] = seed.FirstName,
                ["LastName"] = seed.LastName,
                ["AvatarUrl"] = seed.AvatarUrl
            });

        return Task.FromResult<SocialUserInfo?>(socialUser);
    }
}
