using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// POST /api/auth/reset-password must not tell an unauthenticated caller anything about the account: an unknown
/// email and a known email with a bad token answer identically, and the new password is never compared with the
/// current one before the token has been proven (that comparison used to answer 409 for "this is your current
/// password" and bypassed the lockout counter).
/// </summary>
[Collection("AuthPostgres")]
public sealed class ResetPasswordOracleTests
{
    [Fact(DisplayName = "reset-password answers an unknown email and a known email with a bad token identically")]
    [Trait("Category", "Security")]
    public async Task ResetPassword_UnknownEmail_LooksLikeABadToken()
    {
        var email = $"oracle-{Guid.NewGuid():N}@example.test";
        var seed = new SocialIdentitySeed(
            RawToken: $"token-{Guid.NewGuid():N}",
            ProviderName: "Google",
            ProviderId: $"provider-{Guid.NewGuid():N}",
            Email: email,
            IsEmailVerified: true);

        await using var database = new PostgresAuthTestFactory(socialSeeds: new[] { seed });
        await database.PrepareDatabaseAsync();
        var client = database.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/social/google", new { idToken = seed.RawToken })).StatusCode);

        const string password = "MyStrong!Pass93x";
        var known = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, token = "not-a-real-token", newPassword = password, confirmPassword = password });
        var unknown = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email = $"nobody-{Guid.NewGuid():N}@example.test", token = "not-a-real-token", newPassword = password, confirmPassword = password });

        Assert.Equal(HttpStatusCode.BadRequest, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }
}
