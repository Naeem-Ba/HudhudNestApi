using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Auth;

public sealed class PhoneAuthFlowIntegrationTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private readonly HttpClient _client;
    public PhoneAuthFlowIntegrationTests(PhoneAuthWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RegistrationThenPasswordLogin_IssuesSessions_AndLegacyOtpLoginIsGone()
    {
        var phone = UniquePhone();
        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var challengeId = await Property<Guid>(send, "challengeId");

        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = "SecurePass9",
            firstName = "Naeem",
            lastName = "Bazzazeh"
        });
        Assert.True(registration.StatusCode == HttpStatusCode.OK,
            $"Registration failed: {registration.StatusCode}: {await registration.Content.ReadAsStringAsync()}");
        Assert.False(string.IsNullOrWhiteSpace(await Property<string>(registration, "accessToken")));

        // RELEASE-BLOCKERS-AR.md B-13: the refresh token is no longer readable from the
        // response body — it travels only in the HttpOnly refresh_token cookie now.
        Assert.True(string.IsNullOrWhiteSpace(await Property<string>(registration, "refreshToken")));
        Assert.True(HasRefreshTokenCookie(registration), "Registration did not set the refresh_token cookie.");
        // Production incident (2026-09): see RefreshTokenCookie.cs's doc comment — Chrome/Edge
        // confirmed live to drop this cookie outright without the Partitioned (CHIPS) attribute
        // because the real deployed frontend/backend are different registrable domains.
        Assert.True(RefreshTokenCookieIsPartitioned(registration), "refresh_token cookie is missing the Partitioned attribute.");

        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = "SecurePass9" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(HasRefreshTokenCookie(login), "Login did not set the refresh_token cookie.");
        Assert.False(string.IsNullOrWhiteSpace(await Property<string>(login, "accessToken")));

        var legacy = await _client.PostAsJsonAsync("/api/auth/phone/verify", new { phoneNumber = phone, code = DeterministicOtpService.ValidOtp });
        Assert.Equal(HttpStatusCode.Gone, legacy.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AnonymousResetSend_UsesSameContract_ForKnownAndUnknownPhones()
    {
        var unknown = await _client.PostAsJsonAsync("/api/auth/phone/password-reset/send-otp", new { phoneNumber = UniquePhone() });
        var anotherUnknown = await _client.PostAsJsonAsync("/api/auth/phone/password-reset/send-otp", new { phoneNumber = UniquePhone() });
        Assert.Equal(unknown.StatusCode, anotherUnknown.StatusCode);
        Assert.Equal(await Message(unknown), await Message(anotherUnknown));
    }

    private static async Task<T> Property<T>(HttpResponseMessage response, string name)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in json.RootElement.EnumerateObject())
            if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return item.Value.Deserialize<T>()!;
        throw new InvalidOperationException($"Missing JSON property '{name}'.");
    }

    private static Task<string> Message(HttpResponseMessage response) => Property<string>(response, "message");

    private static bool HasRefreshTokenCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
        cookies.Any(cookie => cookie.StartsWith("refresh_token=", StringComparison.Ordinal));

    private static bool RefreshTokenCookieIsPartitioned(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
        cookies.Any(cookie => cookie.StartsWith("refresh_token=", StringComparison.Ordinal) && cookie.Contains("Partitioned"));
    private static string UniquePhone() => $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
}
