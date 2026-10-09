using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Owner report 2026-10-09: "send activation link" in account settings could be pressed any number of
/// times in a day. POST /api/auth/email/add had no rate-limit policy at all, and the anonymous resend
/// endpoint's limit is per caller address, not per account. An account may be sent at most
/// <c>EmailConfirmationSendPolicy.MaxSends</c> confirmation mails per rolling 24 hours, whichever door
/// asks. Its own class so that it has its own host, cache and send-otp rate-limit budget.
/// </summary>
public sealed class ConfirmationMailLimitTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9";
    private const int MaxSends = 3;

    private readonly HttpClient _client;

    public ConfirmationMailLimitTests(PhoneAuthWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact(DisplayName = "The fourth 'send activation link' within 24 hours is refused with 429 and saves nothing")]
    [Trait("Category", "Security")]
    public async Task FourthAddEmail_IsRefused_AndDoesNotTouchTheAccount()
    {
        var phone = await RegisterAsync();

        for (var send = 1; send <= MaxSends; send++)
        {
            // Each accepted add rotates the security stamp, so every attempt needs a fresh token.
            var accepted = await AddEmailAsync(await LoginAsync(phone));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        var access = await LoginAsync(phone);
        var refused = await AddEmailAsync(access);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("EMAIL_CONFIRMATION_LIMIT_REACHED", (await Json(refused)).GetProperty("code").GetString());
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After must say when to come back.");

        // A refused request must not have rotated the security stamp: the token issued just before it
        // is still valid. (Saving the address signs the account's other sessions out; a request that
        // cannot send must not do that.)
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/users/me", access)).StatusCode);
    }

    private async Task<string> RegisterAsync()
    {
        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await Json(send)).GetProperty("challengeId").GetGuid();
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = Password,
            firstName = "Mail",
            lastName = "Limit"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        return phone;
    }

    private async Task<string> LoginAsync(string phone)
    {
        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await Json(login)).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> AddEmailAsync(string access) =>
        SendAsync(
            HttpMethod.Post,
            "/api/auth/email/add",
            access,
            new { email = $"limit-{Guid.NewGuid():N}@example.com", language = "ar" });

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
