using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// SMS pumping / international revenue share fraud: any well-formed E.164 number worldwide used to be
/// accepted for a registration OTP, and the limits are per client IP, so an attacker with many IPs could
/// make the API send unlimited paid SMS to premium-rate numbers. SmsProvider:AllowedCountryCodes limits
/// the numbers a NEW registration / number change may target. Existing accounts must never be locked out
/// by it (a user who registered with a foreign number before the list existed can still reset a password).
/// </summary>
public sealed class PhoneSmsCostControlTests : IDisposable
{
    private const string Password = "SecurePass9";
    private static int _seed = Environment.TickCount & 0xFFFFFF;

    private readonly PhoneLoginAuditFactory _open = new();
    private readonly PhoneLoginAuditFactory _syriaOnly = new() { AllowedCountryCodes = ["+963"] };
    private readonly PhoneLoginAuditFactory _syriaAndGermany = new() { AllowedCountryCodes = ["+963", "+49"] };

    private static string SyrianPhone() => $"+9639{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
    private static string GermanPhone() => $"+49151{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
    private static string NewIp() => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Post(PhoneLoginAuditFactory factory, string path, object body)
    {
        using var client = factory.CreateClient(new() { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Test-Ip", NewIp());
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, json);
    }

    private static string? Code(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in json.EnumerateObject())
            if (p.Name is "code" or "errorCode" && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        return null;
    }

    private static async Task Prepare(params PhoneLoginAuditFactory[] factories)
    {
        foreach (var factory in factories) await factory.PrepareDatabaseAsync();
    }

    [Fact]
    public async Task NoAllowList_KeepsTodaysBehavior_AnyE164NumberCanRegister()
    {
        await Prepare(_open);
        var phone = GermanPhone();

        var send = await Post(_open, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone });

        Assert.Equal(HttpStatusCode.OK, send.Status);
        Assert.Equal(1, _open.Sms.SendCount);
    }

    [Fact]
    public async Task AllowList_RejectsARegistrationOtp_ForAnotherCountry_AndSendsNoSms()
    {
        await Prepare(_syriaOnly);

        var send = await Post(_syriaOnly, "/api/auth/phone/registration/send-otp", new { phoneNumber = GermanPhone() });

        Assert.Equal(HttpStatusCode.BadRequest, send.Status);
        Assert.Equal("PHONE_COUNTRY_NOT_SUPPORTED", Code(send.Json));
        Assert.Equal(0, _syriaOnly.Sms.SendCount);
    }

    [Fact]
    public async Task AllowList_StillSendsToAnAllowedCountry()
    {
        await Prepare(_syriaAndGermany);

        var syrian = await Post(_syriaAndGermany, "/api/auth/phone/registration/send-otp", new { phoneNumber = SyrianPhone() });
        var german = await Post(_syriaAndGermany, "/api/auth/phone/registration/send-otp", new { phoneNumber = GermanPhone() });

        Assert.Equal(HttpStatusCode.OK, syrian.Status);
        Assert.Equal(HttpStatusCode.OK, german.Status);
        Assert.Equal(2, _syriaAndGermany.Sms.SendCount);
    }

    [Fact]
    public async Task AllowList_DoesNotLockOutAnExistingAccount_WithAForeignNumber()
    {
        await Prepare(_open, _syriaOnly);
        var phone = GermanPhone();

        // registered while there was no restriction
        var send = await Post(_open, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = Guid.Parse(send.Json.GetProperty("challengeId").GetString()!);
        var register = await Post(_open, "/api/auth/phone/registration/verify",
            new { challengeId, code = _open.Sms.LastCodeFor(phone), password = Password, firstName = "Old", lastName = "User" });
        Assert.Equal(HttpStatusCode.OK, register.Status);

        var login = await Post(_syriaOnly, "/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        var reset = await Post(_syriaOnly, "/api/auth/phone/password-reset/send-otp", new { phoneNumber = phone });

        Assert.Equal(HttpStatusCode.OK, login.Status);
        Assert.Equal(HttpStatusCode.OK, reset.Status);
        Assert.Equal(1, _syriaOnly.Sms.SendCount);
    }

    [Fact]
    public async Task AllowList_AppliesTheSameWayToARegistrationOtp_WhateverTheNumberIsAlreadyKnown()
    {
        // The answer depends only on the public prefix, so it cannot be used to probe accounts.
        await Prepare(_syriaOnly);
        var first = await Post(_syriaOnly, "/api/auth/phone/registration/send-otp", new { phoneNumber = "+12025550123" });
        var second = await Post(_syriaOnly, "/api/auth/phone/registration/send-otp", new { phoneNumber = "+12025550123" });

        Assert.Equal("PHONE_COUNTRY_NOT_SUPPORTED", Code(first.Json));
        Assert.Equal("PHONE_COUNTRY_NOT_SUPPORTED", Code(second.Json));
    }

    public void Dispose()
    {
        _open.Dispose();
        _syriaOnly.Dispose();
        _syriaAndGermany.Dispose();
    }
}
