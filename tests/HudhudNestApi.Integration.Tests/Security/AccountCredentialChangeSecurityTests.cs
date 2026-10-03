using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Security audit 2026-10-03, findings F-02 and F-03, both about what a stolen access token can do
/// to the account it belongs to.
/// </summary>
public sealed class AccountCredentialChangeSecurityTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9";
    private const string NewPassword = "Zx9!kPq2#LmVb7";

    private readonly HttpClient _client;

    public AccountCredentialChangeSecurityTests(PhoneAuthWebApplicationFactory factory)
        => _client = factory.CreateClient();

    /// <summary>
    /// F-02: POST /api/users/me/change-password verifies the "current password" through
    /// UserManager.ChangePasswordAsync, which -- unlike every login path -- never counts a failure towards
    /// Identity lockout, and the endpoint has no rate-limit policy. A thief holding a 30-minute access
    /// token could therefore guess the real password without limit, and a guessed password outlives the
    /// token and the refresh-token revocation. After the lockout threshold the correct password must
    /// no longer be accepted either.
    /// </summary>
    [Fact(DisplayName = "Change-password counts wrong current passwords towards lockout")]
    [Trait("Category", "Security")]
    public async Task ChangePassword_WrongCurrentPassword_IsCountedTowardsLockout()
    {
        var (phone, access) = await RegisterAndLoginAsync();

        // MaxFailedAccessAttempts is 5 (PersistenceInfrastructureRegistration). The rate limiter
        // (see ChangePassword_IsRateLimited) would also stop a sixth call, so lockout is observed
        // through the account's other door: the correct password no longer signs in.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrong = await ChangePasswordAsync(access, "Wrong-Guess-" + attempt, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password });

        Assert.NotEqual(HttpStatusCode.OK, login.StatusCode);
    }

    /// <summary>
    /// F-02/F-06: the endpoint is also rate limited (auth-password-change: 5 per hour per user), so
    /// guessing is bounded even before the lockout threshold is considered.
    /// </summary>
    [Fact(DisplayName = "Change-password is rate limited per user")]
    [Trait("Category", "Security")]
    public async Task ChangePassword_IsRateLimited()
    {
        var (_, access) = await RegisterAndLoginAsync();

        for (var attempt = 1; attempt <= 5; attempt++)
            await ChangePasswordAsync(access, "Wrong-Guess-" + attempt, NewPassword);

        var sixth = await ChangePasswordAsync(access, "Wrong-Guess-6", NewPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
    }

    /// <summary>
    /// F-03: the JWT pipeline validates the token's security stamp against a 5-minute cache
    /// (CachedSecurityStampValidator). Changing the password rotates the stamp but, unlike logout and
    /// refresh-token-reuse handling, does not invalidate that cache entry -- so the token the owner just
    /// "secured" the account against keeps working for up to five more minutes.
    /// </summary>
    [Fact(DisplayName = "Access token issued before a password change is rejected straight afterwards")]
    [Trait("Category", "Security")]
    public async Task ChangePassword_EndsTheAccessTokenImmediately()
    {
        var (_, access) = await RegisterAndLoginAsync();

        // Prime the stamp cache the way any ordinary request would.
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(access)).StatusCode);

        var change = await ChangePasswordAsync(access, Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(access)).StatusCode);
    }

    private async Task<(string Phone, string Access)> RegisterAndLoginAsync()
    {
        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await Json(send)).GetProperty("challengeId").GetGuid();
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = Password,
            firstName = "Audit",
            lastName = "Subject"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (phone, (await Json(login)).GetProperty("accessToken").GetString()!);
    }

    private Task<HttpResponseMessage> ChangePasswordAsync(string access, string current, string next)
        => Send(HttpMethod.Post, "/api/users/me/change-password", access, new { currentPassword = current, newPassword = next });

    private Task<HttpResponseMessage> GetMeAsync(string access) => Send(HttpMethod.Get, "/api/users/me", access);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
