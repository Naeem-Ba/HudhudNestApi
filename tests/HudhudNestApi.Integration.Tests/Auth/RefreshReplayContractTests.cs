using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// The refresh-replay contract the Staging smoke journey relies on (API #398): replaying a just-rotated token is
/// answered with a fresh sibling token and ends nothing; replaying it after Jwt:RefreshReuseGraceSeconds is reuse,
/// refused with 401 and ends the whole session family. The Staging smoke asserted the old behaviour (401 at once)
/// and failed the Production Gate after #398 merged; this pins the contract in the same shape as that journey.
/// </summary>
public sealed class RefreshReplayContractTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9!";
    private readonly PhoneAuthWebApplicationFactory _factory;

    public RefreshReplayContractTests(PhoneAuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Replay inside the grace window gets a new sibling token and keeps the session; after it, reuse is refused and ends the family")]
    [Trait("Category", "Integration")]
    public async Task Replay_IsServedInsideTheWindow_AndRefusedAfterIt()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:RefreshReuseGraceSeconds"] = "3"
                })));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await Json(send)).GetProperty("challengeId").GetGuid();
        var registration = await client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = Password,
            firstName = "Naeem",
            lastName = "Bazzazeh"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var first = RefreshCookie(login);

        var rotation = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = first });
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        var rotated = RefreshCookie(rotation);
        var rotatedAccess = (await Json(rotation)).GetProperty("accessToken").GetString()!;
        Assert.NotEqual(first, rotated);
        Assert.Equal(HttpStatusCode.OK, (await Me(client, rotatedAccess)).StatusCode);

        // Inside the window: 200, a token that is neither of the two earlier ones, and nothing is revoked.
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = first });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var sibling = RefreshCookie(replay);
        Assert.NotEqual(first, sibling);
        Assert.NotEqual(rotated, sibling);
        var siblingAccess = (await Json(replay)).GetProperty("accessToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Me(client, siblingAccess)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Me(client, rotatedAccess)).StatusCode);

        // After the window: reuse. Presented until it is refused, as the smoke journey does.
        HttpStatusCode last = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 12 && last != HttpStatusCode.Unauthorized; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            last = (await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = first })).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, last);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Me(client, rotatedAccess)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Me(client, siblingAccess)).StatusCode);
    }

    private static string RefreshCookie(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("refresh_token=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(cookie.Split(';')[0]["refresh_token=".Length..]);
    }

    private static Task<HttpResponseMessage> Me(HttpClient client, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
