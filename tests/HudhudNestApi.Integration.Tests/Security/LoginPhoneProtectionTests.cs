using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Phone confirmation audit 2026-10-10. With nothing but an access token, PUT /api/users/me {phoneNumber}
/// used to replace the account's LOGIN phone (the new number was stored unconfirmed), and the phone
/// password-reset flow treated any number held by an account as eligible whether it was confirmed or not.
/// Together: send-otp, verify and confirm on the attacker's own number returned 200, a new password was
/// set, and the attacker signed in while the owner's credentials stopped working.
///
/// Two rules close it:
///   1. a confirmed login phone cannot be replaced or removed from the profile (first two tests);
///   2. only a confirmed number can reset a password (last test).
/// Each test fails if its rule is lifted. The third test guards what must keep working: the app sends the
/// phone field on every profile save.
/// Its own class so that it has its own host and a fresh auth-password-reset budget (3 per hour per caller):
/// the tests together send three reset codes.
/// </summary>
public sealed class LoginPhoneProtectionTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9";
    private const string EmailAccountPassword = "SecurePass9!";

    // One phone-registered account for the whole class: registration spends from the send-otp (3) and
    // verify-otp (5) budgets of this host, and none of these tests changes that account's phone.
    private static readonly SemaphoreSlim OwnerLock = new(1, 1);
    private static (string Phone, string Access)? _owner;

    private readonly HttpClient _client;

    public LoginPhoneProtectionTests(PhoneAuthWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact(DisplayName = "A confirmed login phone cannot be replaced from the profile, so the takeover chain is cut at its first step")]
    [Trait("Category", "Security")]
    public async Task ProfileUpdate_CannotReplaceAConfirmedLoginPhone()
    {
        var (phone, access) = await RegisterByPhoneAsync();
        var attackerPhone = NextPhone();

        var replace = await SendAsync(HttpMethod.Put, "/api/users/me", access, new { phoneNumber = attackerPhone });

        await AssertStatusAsync(HttpStatusCode.UnprocessableEntity, replace);
        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", access));
        Assert.Equal(phone, me.GetProperty("phoneNumber").GetString());
        Assert.True(me.GetProperty("phoneConfirmed").GetBoolean(), "The refused request must not unconfirm the owner's phone.");

        // The rest of the chain: the attacker's number holds no account and no reset code, so it cannot
        // produce a reset token, and the owner's own credentials keep working.
        var send = await _client.PostAsJsonAsync("/api/auth/phone/password-reset/send-otp", new { phoneNumber = attackerPhone });
        await AssertStatusAsync(HttpStatusCode.OK, send);
        var verify = await _client.PostAsJsonAsync("/api/auth/phone/password-reset/verify", new
        {
            challengeId = (await Json(send)).GetProperty("challengeId").GetGuid(),
            code = DeterministicOtpService.ValidOtp
        });
        Assert.NotEqual(HttpStatusCode.OK, verify.StatusCode);

        await AssertStatusAsync(HttpStatusCode.OK, await LoginByPhoneAsync(phone, Password));
    }

    [Fact(DisplayName = "A confirmed login phone cannot be removed from the profile")]
    [Trait("Category", "Security")]
    public async Task ProfileUpdate_CannotClearAConfirmedLoginPhone()
    {
        var (phone, access) = await RegisterByPhoneAsync();

        var clear = await SendAsync(HttpMethod.Put, "/api/users/me", access, new { phoneNumber = "" });

        await AssertStatusAsync(HttpStatusCode.UnprocessableEntity, clear);
        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", access));
        Assert.Equal(phone, me.GetProperty("phoneNumber").GetString());
        Assert.True(me.GetProperty("phoneConfirmed").GetBoolean());
    }

    [Fact(DisplayName = "Saving the profile with the same phone keeps it confirmed")]
    [Trait("Category", "Security")]
    public async Task ProfileUpdate_WithTheSamePhone_IsAccepted_AndKeepsItConfirmed()
    {
        var (phone, access) = await RegisterByPhoneAsync();
        var sameNumberWithPadding = $"  {phone} ";

        // The app sends the phone field on every profile save, so this must stay a no-op.
        foreach (var sent in new[] { phone, sameNumberWithPadding })
        {
            var save = await SendAsync(HttpMethod.Put, "/api/users/me", access, new { phoneNumber = sent, displayName = "Saved with the phone field" });
            await AssertStatusAsync(HttpStatusCode.NoContent, save);
        }

        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", access));
        Assert.Equal(phone, me.GetProperty("phoneNumber").GetString());
        Assert.True(me.GetProperty("phoneConfirmed").GetBoolean());
        Assert.Equal("Saved with the phone field", me.GetProperty("displayName").GetString());
    }

    [Fact(DisplayName = "A phone typed into the profile is unconfirmed and cannot reset the password")]
    [Trait("Category", "Security")]
    public async Task PasswordReset_RefusesAnUnconfirmedPhone_ButServesAConfirmedOne()
    {
        // Control: a number proven by OTP at registration does produce a reset token. Without it the
        // refusal below would also pass if the whole reset flow were simply broken.
        var (confirmedPhone, _) = await RegisterByPhoneAsync();
        await AssertStatusAsync(HttpStatusCode.OK, await VerifyResetAsync(confirmedPhone));

        // An e-mail account that only typed the number into its profile (the attacker's position: a
        // token, no proof of the number).
        var emailAccess = await RegisterByEmailAsync();
        var typedPhone = NextPhone();
        var save = await SendAsync(HttpMethod.Put, "/api/users/me", emailAccess, new { phoneNumber = typedPhone });
        await AssertStatusAsync(HttpStatusCode.NoContent, save);
        var me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", emailAccess));
        Assert.Equal(typedPhone, me.GetProperty("phoneNumber").GetString());
        Assert.False(me.GetProperty("phoneConfirmed").GetBoolean());

        // A number nobody has proven is only a claim, so it can still be corrected from the profile.
        var corrected = NextPhone();
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, "/api/users/me", emailAccess, new { phoneNumber = corrected }));
        me = await Json(await SendAsync(HttpMethod.Get, "/api/users/me", emailAccess));
        Assert.Equal(corrected, me.GetProperty("phoneNumber").GetString());

        var verify = await VerifyResetAsync(corrected);

        Assert.NotEqual(HttpStatusCode.OK, verify.StatusCode);
        Assert.DoesNotContain("confirmationToken", await verify.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> VerifyResetAsync(string phone)
    {
        var send = await _client.PostAsJsonAsync("/api/auth/phone/password-reset/send-otp", new { phoneNumber = phone });
        await AssertStatusAsync(HttpStatusCode.OK, send);
        return await _client.PostAsJsonAsync("/api/auth/phone/password-reset/verify", new
        {
            challengeId = (await Json(send)).GetProperty("challengeId").GetGuid(),
            code = DeterministicOtpService.ValidOtp
        });
    }

    private async Task<(string Phone, string Access)> RegisterByPhoneAsync()
    {
        await OwnerLock.WaitAsync();
        try
        {
            return _owner ??= await RegisterNewPhoneAccountAsync();
        }
        finally
        {
            OwnerLock.Release();
        }
    }

    private async Task<(string Phone, string Access)> RegisterNewPhoneAccountAsync()
    {
        var phone = NextPhone();
        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId = (await Json(send)).GetProperty("challengeId").GetGuid(),
            code = DeterministicOtpService.ValidOtp,
            password = Password,
            firstName = "Phone",
            lastName = "Owner"
        });
        await AssertStatusAsync(HttpStatusCode.OK, registration);

        var login = await LoginByPhoneAsync(phone, Password);
        await AssertStatusAsync(HttpStatusCode.OK, login);
        return (phone, (await Json(login)).GetProperty("accessToken").GetString()!);
    }

    private async Task<string> RegisterByEmailAsync()
    {
        var email = $"login-phone-{Guid.NewGuid():N}@example.com";
        var registration = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Mail",
            lastName = "Owner",
            email,
            password = EmailAccountPassword,
            privacyPolicyAccepted = true,
            privacyPolicyVersion = "2026-10-01",
            termsAccepted = true,
            termsVersion = "2026-10-01",
            consentSource = "Web"
        });
        await AssertStatusAsync(HttpStatusCode.Created, registration);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = EmailAccountPassword });
        await AssertStatusAsync(HttpStatusCode.OK, login);
        return (await Json(login)).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> LoginByPhoneAsync(string phone, string password) =>
        _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password });

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
