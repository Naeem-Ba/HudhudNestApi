using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Phone confirmation audit 2026-10-10, phase 2. A phone number typed into a profile is stored unconfirmed
/// and, before this change, locked the number for everybody else: the person who really owns it could neither
/// register with it nor attach it to their own account, and the account that typed it could not confirm it
/// either (the change flow treated its own stored number as "taken"). A one-time code proves control of a
/// number, a typed number proves nothing, so the code wins:
///   * registration with a number only an unconfirmed account holds succeeds and releases that claim;
///   * the change flow confirms the number the account itself holds unconfirmed;
///   * the change flow takes a number another account only claimed, and releases that claim.
/// A number held CONFIRMED still blocks, which the existing duplicate-registration and phone-change tests keep covering.
/// Its own class so that it has its own host: the three tests together use exactly the send-otp budget (3 per
/// 15 minutes per caller).
/// </summary>
public sealed class PhoneProofBeatsClaimTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string PhonePassword = "SecurePass9";
    private const string EmailAccountPassword = "SecurePass9!";

    private readonly HttpClient _client;

    public PhoneProofBeatsClaimTests(PhoneAuthWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact(DisplayName = "The real owner can register with a number someone else only typed into a profile")]
    [Trait("Category", "Security")]
    public async Task Registration_ByTheRealOwner_ReleasesAnUnconfirmedClaim()
    {
        var number = NextPhone();
        var squatter = await RegisterByEmailAsync();
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, "/api/users/me", squatter.Access, new { phoneNumber = number }));

        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = number });
        await AssertStatusAsync(HttpStatusCode.OK, send);
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId = (await Json(send)).GetProperty("challengeId").GetGuid(),
            code = DeterministicOtpService.ValidOtp,
            password = PhonePassword,
            firstName = "Real",
            lastName = "Owner"
        });

        await AssertStatusAsync(HttpStatusCode.OK, registration);
        await AssertStatusAsync(HttpStatusCode.OK, await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = number, password = PhonePassword }));
        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", squatter.Access));
        Assert.Equal(JsonValueKind.Null, me.GetProperty("phoneNumber").ValueKind);
        Assert.False(me.GetProperty("phoneConfirmed").GetBoolean());
    }

    [Fact(DisplayName = "An account can confirm the number it already holds unconfirmed")]
    [Trait("Category", "Security")]
    public async Task PhoneChange_ConfirmsTheNumberTheAccountAlreadyHolds()
    {
        var number = NextPhone();
        var account = await RegisterByEmailAsync();
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, "/api/users/me", account.Access, new { phoneNumber = number }));

        await AssertStatusAsync(HttpStatusCode.OK, await ChangePhoneAsync(account, number));

        // Confirming ends the account's sessions (security stamp), so look again through a fresh sign-in.
        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", await LoginByEmailAsync(account.Email)));
        Assert.Equal(number, me.GetProperty("phoneNumber").GetString());
        Assert.True(me.GetProperty("phoneConfirmed").GetBoolean());
    }

    [Fact(DisplayName = "The phone change takes a number another account only claimed, and releases that claim")]
    [Trait("Category", "Security")]
    public async Task PhoneChange_ToANumberOnlyClaimedElsewhere_ReleasesTheClaim()
    {
        var number = NextPhone();
        var claimant = await RegisterByEmailAsync();
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, "/api/users/me", claimant.Access, new { phoneNumber = number }));
        var owner = await RegisterByEmailAsync();

        await AssertStatusAsync(HttpStatusCode.OK, await ChangePhoneAsync(owner, number));

        var ownerMe = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", await LoginByEmailAsync(owner.Email)));
        Assert.Equal(number, ownerMe.GetProperty("phoneNumber").GetString());
        Assert.True(ownerMe.GetProperty("phoneConfirmed").GetBoolean());
        var claimantMe = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", claimant.Access));
        Assert.Equal(JsonValueKind.Null, claimantMe.GetProperty("phoneNumber").ValueKind);
    }

    /// <summary>Sends a code to <paramref name="number"/> and completes the change with it, as the account's owner.</summary>
    private async Task<HttpResponseMessage> ChangePhoneAsync((string Email, string Access) account, string number)
    {
        var send = await SendAsync(HttpMethod.Post, "/api/auth/phone/change/send-otp", account.Access, new { phoneNumber = number });
        await AssertStatusAsync(HttpStatusCode.OK, send);
        return await SendAsync(HttpMethod.Post, "/api/auth/phone/change/verify", account.Access, new
        {
            challengeId = (await Json(send)).GetProperty("challengeId").GetGuid(),
            code = DeterministicOtpService.ValidOtp,
            currentPassword = EmailAccountPassword
        });
    }

    private async Task<(string Email, string Access)> RegisterByEmailAsync()
    {
        var email = $"proof-claim-{Guid.NewGuid():N}@example.com";
        var registration = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Mail",
            lastName = "Account",
            email,
            password = EmailAccountPassword,
            privacyPolicyAccepted = true,
            privacyPolicyVersion = "2026-10-01",
            termsAccepted = true,
            termsVersion = "2026-10-01",
            consentSource = "Web"
        });
        await AssertStatusAsync(HttpStatusCode.Created, registration);
        return (email, await LoginByEmailAsync(email));
    }

    private async Task<string> LoginByEmailAsync(string email)
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = EmailAccountPassword });
        await AssertStatusAsync(HttpStatusCode.OK, login);
        return (await Json(login)).GetProperty("accessToken").GetString()!;
    }

    private static string NextPhone() => $"+49{Math.Abs(Guid.NewGuid().GetHashCode() % 1_000_000_000L) + 1_000_000_000L:D10}";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
