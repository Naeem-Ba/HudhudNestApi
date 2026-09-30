using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;
using HudhudNestApi.Security.Staging;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Covers RateLimitingRegistration's "send-otp"/"verify-otp" exemption for Staging smoke-suite
/// traffic (see the doc comment on "verify-otp" in that file). Reproduces the real production
/// bug directly: Staging collapses every caller behind one shared IP, so the mandatory smoke
/// journey's required call pattern -- more calls than an ordinary visitor could ever need --
/// would otherwise 429 on its own, every run. Each test builds its own Staging host so the
/// in-memory rate limiter (state lives entirely inside that one TestServer process, unlike the
/// Redis-backed policies elsewhere in this suite) starts from a clean window.
/// </summary>
public sealed class StagingSmokeRateLimitExemptionTests
{
    private const string ValidSecret = "staging-smoke-test-secret-at-least-32-characters";

    [Fact(DisplayName = "send-otp: an ordinary Staging caller without the smoke secret still hits the shared PermitLimit=3")]
    public async Task SendOtp_WithoutSecret_IsStillRateLimited()
    {
        using var app = TestApplication.CreateStaging();
        using var client = app.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsJsonAsync(
                "/api/auth/phone/registration/send-otp", new { phoneNumber = UniquePhone() });
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact(DisplayName = "send-otp: the Staging smoke secret exempts the caller from the shared limit entirely")]
    public async Task SendOtp_WithValidStagingSmokeSecret_IsNeverRateLimited()
    {
        using var app = TestApplication.CreateStaging();
        using var client = app.CreateClient();

        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/auth/phone/registration/send-otp")
            {
                Content = JsonContent.Create(new { phoneNumber = UniquePhone() })
            };
            request.Headers.Add(StagingTestSupportAuthorization.SecretHeaderName, ValidSecret);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(DisplayName = "verify-otp: an ordinary Staging caller without the smoke secret hits the shared PermitLimit=5, reproducing the real 429 behind \"Consumed OTP was reusable\"")]
    public async Task VerifyOtp_WithoutSecret_IsStillRateLimited()
    {
        using var app = TestApplication.CreateStaging();
        using var client = app.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++)
        {
            var challengeId = await SendOtpAsync(client, UniquePhone(), withSecret: true);

            last = await client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
            {
                challengeId,
                code = "000000",
                password = "SecurePass9",
                firstName = "E2E",
                lastName = "RateLimit"
            });
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact(DisplayName = "verify-otp: the Staging smoke secret exempts the caller from the shared limit entirely")]
    public async Task VerifyOtp_WithValidStagingSmokeSecret_IsNeverRateLimited()
    {
        using var app = TestApplication.CreateStaging();
        using var client = app.CreateClient();

        for (var i = 0; i < 6; i++)
        {
            var challengeId = await SendOtpAsync(client, UniquePhone(), withSecret: true);

            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/auth/phone/registration/verify")
            {
                Content = JsonContent.Create(new
                {
                    challengeId,
                    code = "000000",
                    password = "SecurePass9",
                    firstName = "E2E",
                    lastName = "RateLimit"
                })
            };
            request.Headers.Add(StagingTestSupportAuthorization.SecretHeaderName, ValidSecret);

            using var response = await client.SendAsync(request);

            // The wrong OTP code is expected to be rejected on business grounds (400) every
            // time -- the point being verified is that it is never rejected as 429.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact(DisplayName = "send-otp: an incorrect X-Staging-Smoke-Secret value does not exempt the caller")]
    public async Task SendOtp_WithWrongSecret_IsStillRateLimited()
    {
        using var app = TestApplication.CreateStaging();
        using var client = app.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "/api/auth/phone/registration/send-otp")
            {
                Content = JsonContent.Create(new { phoneNumber = UniquePhone() })
            };
            request.Headers.Add(StagingTestSupportAuthorization.SecretHeaderName, "not-the-real-secret");

            last = await client.SendAsync(request);
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    private static async Task<Guid> SendOtpAsync(HttpClient client, string phone, bool withSecret)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/auth/phone/registration/send-otp")
        {
            Content = JsonContent.Create(new { phoneNumber = phone })
        };
        if (withSecret)
            request.Headers.Add(StagingTestSupportAuthorization.SecretHeaderName, ValidSecret);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("challengeId").GetGuid();
    }

    private static string UniquePhone() =>
        $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
}
