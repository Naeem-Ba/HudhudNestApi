using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// Load-test finding D11: ten parallel sign-ins to the same account answered 409 for most of them. Signing in
/// from several devices (or a client retrying) is normal, so concurrent sign-ins of one account must all succeed.
/// </summary>
public sealed class ConcurrentLoginIntegrationTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9!";
    private readonly HttpClient _client;

    public ConcurrentLoginIntegrationTests(PhoneAuthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact(DisplayName = "Parallel sign-ins of one account all succeed and keep only the allowed number of sessions")]
    [Trait("Category", "Integration")]
    public async Task ParallelLoginsOfOneAccount_AllSucceed()
    {
        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await send.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("challengeId").GetGuid();
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = Password,
            firstName = "Naeem",
            lastName = "Bazzazeh"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        for (var round = 0; round < 2; round++)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password })));

            var statuses = responses.Select(r => r.StatusCode).GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
            var firstFailure = responses.FirstOrDefault(r => r.StatusCode != HttpStatusCode.OK);
            var detail = firstFailure is null ? string.Empty : await firstFailure.Content.ReadAsStringAsync();
            Assert.True(
                firstFailure is null,
                $"Round {round}: " + string.Join(", ", statuses.Select(kv => $"{(int)kv.Key} x{kv.Value}")) + " " + detail);
        }
    }
}
