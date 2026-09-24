using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Domain.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// Multi-channel OTP: the phone number stays the identity, the channel is only how one challenge's code is
/// delivered. Real PostgreSQL and the real workflow; only the providers behind the channels are fakes.
/// </summary>
public sealed class PhoneMultiChannelOtpTests : IDisposable
{
    private const string Password = "SecurePass9";
    private static int _seed = Environment.TickCount & 0xFFFFFF;

    private readonly PhoneLoginAuditFactory _smsOnly = new();
    private readonly PhoneLoginAuditFactory _all = new()
    {
        TelegramEnabled = true,
        WhatsAppEnabled = true,
        DefaultRecommended = "Sms",
        RecommendedByCountryCode = new() { ["+963"] = "Telegram", ["+49"] = "WhatsApp" }
    };

    private static string SyrianPhone() => $"+9639{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
    private static string GermanPhone() => $"+49151{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
    private static string NewIp() => $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Post(PhoneLoginAuditFactory factory, string path, object body, string? ip = null)
    {
        using var client = factory.CreateClient(new() { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Test-Ip", ip ?? NewIp());
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

    private static Guid Challenge(JsonElement json) => Guid.Parse(json.GetProperty("challengeId").GetString()!);

    private static async Task<PropertyApi.Domain.Auth.Entities.PhoneOtpChallenge> Stored(PhoneLoginAuditFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PhoneOtpChallenges.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private static async Task<int> StoredCount(PhoneLoginAuditFactory factory, string phone)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PhoneOtpChallenges.CountAsync(x => x.NormalizedPhoneNumber == phone);
    }

    private static object Register(Guid challengeId, string? code, string? channel = null) => channel is null
        ? new { challengeId, code, password = Password, firstName = "Multi", lastName = "Channel" }
        : new { challengeId, code, password = Password, firstName = "Multi", lastName = "Channel", channel };

    // ───────────── the picker endpoint ─────────────

    [Fact]
    public async Task Channels_ByDefault_OffersSmsOnly_AndSaysWhyTheOthersAreOff()
    {
        await _smsOnly.PrepareDatabaseAsync();

        var response = await Post(_smsOnly, "/api/auth/phone/channels", new { phoneNumber = GermanPhone() });

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var channels = response.Json.GetProperty("channels").EnumerateArray().ToArray();
        Assert.Equal(["Sms", "Telegram", "WhatsApp"], channels.Select(x => x.GetProperty("channel").GetString()));
        Assert.True(channels[0].GetProperty("available").GetBoolean());
        Assert.False(channels[1].GetProperty("available").GetBoolean());
        Assert.Equal("CHANNEL_DISABLED", channels[1].GetProperty("reason").GetString());
        Assert.False(channels[2].GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Channels_ForASyrianNumber_MarksWhatsAppUnavailableInThatCountry_AndRecommendsTelegram()
    {
        await _all.PrepareDatabaseAsync();

        var response = await Post(_all, "/api/auth/phone/channels", new { phoneNumber = SyrianPhone() });

        var channels = response.Json.GetProperty("channels").EnumerateArray().ToDictionary(x => x.GetProperty("channel").GetString()!);
        Assert.True(channels["Sms"].GetProperty("available").GetBoolean());
        Assert.True(channels["Telegram"].GetProperty("available").GetBoolean());
        Assert.True(channels["Telegram"].GetProperty("recommended").GetBoolean());
        Assert.False(channels["WhatsApp"].GetProperty("available").GetBoolean());
        Assert.Equal("COUNTRY_NOT_SUPPORTED", channels["WhatsApp"].GetProperty("reason").GetString());
        Assert.Contains("+963", channels["WhatsApp"].GetProperty("unavailableCountryCodes").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Channels_ForAGermanNumber_OffersEveryChannel_AndRecommendsWhatsApp()
    {
        await _all.PrepareDatabaseAsync();

        var response = await Post(_all, "/api/auth/phone/channels", new { phoneNumber = GermanPhone() });

        var channels = response.Json.GetProperty("channels").EnumerateArray().ToDictionary(x => x.GetProperty("channel").GetString()!);
        Assert.All(channels.Values, c => Assert.True(c.GetProperty("available").GetBoolean()));
        Assert.True(channels["WhatsApp"].GetProperty("recommended").GetBoolean());
        Assert.False(channels["Sms"].GetProperty("recommended").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("+1")]
    public async Task Channels_NeverBecomesAPhoneValidator_AMissingOrBadNumberGetsTheCountryFreePicture(string? phone)
    {
        await _all.PrepareDatabaseAsync();

        var response = await Post(_all, "/api/auth/phone/channels", new { phoneNumber = phone });

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(3, response.Json.GetProperty("channels").GetArrayLength());
    }

    [Fact]
    public async Task Channels_AnswerIsTheSame_WhetherOrNotTheNumberHasAnAccount()
    {
        await _all.PrepareDatabaseAsync();
        var known = GermanPhone();
        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = known });
        var register = await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(send.Json), _all.Sms.LastCodeFor(known)));
        Assert.Equal(HttpStatusCode.OK, register.Status);

        var withAccount = await Post(_all, "/api/auth/phone/channels", new { phoneNumber = known });
        var without = await Post(_all, "/api/auth/phone/channels", new { phoneNumber = known[..^1] + (known[^1] == '9' ? '0' : '9') });

        Assert.Equal(without.Json.GetRawText(), withAccount.Json.GetRawText());
    }

    // ───────────── sending and verifying per channel ─────────────

    [Fact]
    public async Task Register_ThroughTelegram_StoresTheChannel_SendsNoSms_AndTheCodeVerifies()
    {
        await _all.PrepareDatabaseAsync();
        var phone = SyrianPhone();

        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });

        Assert.Equal(HttpStatusCode.OK, send.Status);
        Assert.Equal("Telegram", send.Json.GetProperty("channel").GetString());
        Assert.Equal(1, _all.Telegram.SendCount);
        Assert.Equal(0, _all.Sms.SendCount);
        var challenge = await Stored(_all, Challenge(send.Json));
        Assert.Equal(OtpChannel.Telegram, challenge.Channel);
        Assert.Equal("req-1", challenge.ProviderRequestId);

        var register = await Post(_all, "/api/auth/phone/registration/verify", Register(challenge.Id, _all.Telegram.LastCodeFor(phone), "Telegram"));

        Assert.Equal(HttpStatusCode.OK, register.Status);
        var login = await Post(_all, "/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.Status);
    }

    [Fact]
    public async Task Send_WithoutAChannel_StillMeansSms_SoOlderClientsKeepWorking()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();

        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone });

        Assert.Equal(1, _all.Sms.SendCount);
        Assert.Equal(0, _all.Telegram.SendCount);
        Assert.Equal(OtpChannel.Sms, (await Stored(_all, Challenge(send.Json))).Channel);
        // ...and a verify that names no channel is accepted whatever the challenge's channel.
        var register = await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(send.Json), _all.Sms.LastCodeFor(phone)));
        Assert.Equal(HttpStatusCode.OK, register.Status);
    }

    [Fact]
    public async Task ACodeIsOnlyAcceptedForItsOwnChannel_AndTheWrongChannelDoesNotBurnAnAttempt()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();
        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });
        var id = Challenge(send.Json);
        var code = _all.Telegram.LastCodeFor(phone);

        var wrongChannel = await Post(_all, "/api/auth/phone/registration/verify", Register(id, code, "Sms"));
        var wrongChannelWhatsApp = await Post(_all, "/api/auth/phone/registration/verify", Register(id, code, "WhatsApp"));

        Assert.Equal(HttpStatusCode.BadRequest, wrongChannel.Status);
        Assert.Equal("OTP_INVALID", Code(wrongChannel.Json));
        Assert.Equal("OTP_INVALID", Code(wrongChannelWhatsApp.Json));
        Assert.Equal(0, (await Stored(_all, id)).AttemptCount);

        var right = await Post(_all, "/api/auth/phone/registration/verify", Register(id, code, "Telegram"));
        Assert.Equal(HttpStatusCode.OK, right.Status);
    }

    [Fact]
    public async Task ACodeIsBoundToItsPhoneAndPurpose_WhateverTheChannel()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();
        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });

        // the registration challenge cannot be used to reset a password
        var reset = await Post(_all, "/api/auth/phone/password-reset/verify",
            new { challengeId = Challenge(send.Json), code = _all.Telegram.LastCodeFor(phone), channel = "Telegram" });

        Assert.Equal(HttpStatusCode.BadRequest, reset.Status);
        Assert.Equal("PASSWORD_RESET_INVALID", Code(reset.Json));
    }

    [Fact]
    public async Task ACodeCannotBeReused_AfterItSucceededOnAnotherChannelChallenge()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();
        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });
        var id = Challenge(send.Json);
        var code = _all.Telegram.LastCodeFor(phone);
        Assert.Equal(HttpStatusCode.OK, (await Post(_all, "/api/auth/phone/registration/verify", Register(id, code, "Telegram"))).Status);

        var again = await Post(_all, "/api/auth/phone/registration/verify", Register(id, code, "Telegram"));

        Assert.Equal(HttpStatusCode.BadRequest, again.Status);
        Assert.Equal("OTP_ALREADY_USED", Code(again.Json));
    }

    [Fact]
    public async Task PasswordReset_WorksThroughTelegram_ForAnExistingAccount()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();
        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(send.Json), _all.Sms.LastCodeFor(phone)));

        var reset = await Post(_all, "/api/auth/phone/password-reset/send-otp", new { phoneNumber = phone, channel = "Telegram" });
        var verify = await Post(_all, "/api/auth/phone/password-reset/verify",
            new { challengeId = Challenge(reset.Json), code = _all.Telegram.LastCodeFor(phone), channel = "Telegram" });

        Assert.Equal(HttpStatusCode.OK, verify.Status);
        Assert.False(string.IsNullOrEmpty(verify.Json.GetProperty("confirmationToken").GetString()));
    }

    [Fact]
    public async Task ChangingTheChannel_NeverCreatesAnotherUser_TheNumberIsTheIdentity()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();
        var first = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });
        var second = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Sms" });
        Assert.Equal(HttpStatusCode.OK, (await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(second.Json), _all.Sms.LastCodeFor(phone), "Sms"))).Status);

        // the Telegram challenge cannot register the same number a second time
        var duplicate = await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(first.Json), _all.Telegram.LastCodeFor(phone), "Telegram"));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.Status);
        Assert.Equal("PHONE_ALREADY_REGISTERED", Code(duplicate.Json));
    }

    // ───────────── availability is enforced on send ─────────────

    [Fact]
    public async Task ADisabledChannel_IsRefused_AndNothingIsSent()
    {
        await _smsOnly.PrepareDatabaseAsync();
        var phone = GermanPhone();

        var send = await Post(_smsOnly, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });

        Assert.Equal(HttpStatusCode.BadRequest, send.Status);
        Assert.Equal("OTP_CHANNEL_UNAVAILABLE", Code(send.Json));
        Assert.Equal(0, _smsOnly.Telegram.SendCount);
        Assert.Equal(0, await StoredCount(_smsOnly, phone));
    }

    [Fact]
    public async Task WhatsApp_IsRefusedForSyria_EvenWhenEnabled_AndTheAnswerDoesNotDependOnTheAccount()
    {
        await _all.PrepareDatabaseAsync();
        var known = SyrianPhone();
        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = known });
        await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(send.Json), _all.Sms.LastCodeFor(known)));

        var forKnown = await Post(_all, "/api/auth/phone/password-reset/send-otp", new { phoneNumber = known, channel = "WhatsApp" });
        var forUnknown = await Post(_all, "/api/auth/phone/password-reset/send-otp", new { phoneNumber = SyrianPhone(), channel = "WhatsApp" });

        Assert.Equal("OTP_CHANNEL_UNAVAILABLE", Code(forKnown.Json));
        Assert.Equal(forKnown.Json.GetRawText(), forUnknown.Json.GetRawText());
        Assert.Equal(0, _all.WhatsApp.SendCount);
    }

    [Fact]
    public async Task AnUnknownChannelName_IsABadRequest()
    {
        await _all.PrepareDatabaseAsync();

        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = GermanPhone(), channel = "Carrier pigeon" });

        Assert.Equal(HttpStatusCode.BadRequest, send.Status);
        Assert.Equal(0, _all.Sms.SendCount);
    }

    // ───────────── provider trouble and anti-enumeration ─────────────

    [Fact]
    public async Task ARecipientThatIsNotReachableOnTelegram_GetsTheSameAnswerAsAnyoneElse()
    {
        await _all.PrepareDatabaseAsync();
        _all.Telegram.Outcome = OtpSendOutcome.RecipientUnreachable;
        var eligible = GermanPhone();
        var takenPhone = GermanPhone();
        var taken = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = takenPhone });
        await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(taken.Json), _all.Sms.LastCodeFor(takenPhone)));

        // registration: one number is free (the provider cannot reach it), the other already has an account (decoy)
        var free = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = eligible, channel = "Telegram" });
        var existing = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = takenPhone, channel = "Telegram" });

        Assert.Equal(HttpStatusCode.OK, free.Status);
        Assert.Equal(HttpStatusCode.OK, existing.Status);
        Assert.Equal(Shape(free.Json), Shape(existing.Json));
        Assert.Equal(1, _all.Telegram.SendCount);   // only the free number reached the provider
    }

    [Fact]
    public async Task ATelegramFailure_ThenTheSmsFallback_WorksWithoutCreatingAnotherUser()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();
        _all.Telegram.Outcome = OtpSendOutcome.RecipientUnreachable;
        var telegram = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });
        Assert.Equal(HttpStatusCode.OK, telegram.Status);
        Assert.Null(_all.Telegram.LastCodeFor(phone));

        var sms = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Sms" });
        var register = await Post(_all, "/api/auth/phone/registration/verify", Register(Challenge(sms.Json), _all.Sms.LastCodeFor(phone), "Sms"));

        Assert.Equal(HttpStatusCode.OK, register.Status);
        Assert.NotEqual(Challenge(telegram.Json), Challenge(sms.Json));
    }

    [Fact]
    public async Task ATelegramOutage_IsReportedAsAProviderProblem_AndTheChallengeIsDiscarded()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();
        _all.Telegram.Outcome = OtpSendOutcome.ProviderUnavailable;

        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });

        Assert.Equal(HttpStatusCode.BadRequest, send.Status);
        Assert.Equal("OTP_PROVIDER_UNAVAILABLE", Code(send.Json));
        Assert.Equal(0, await StoredCount(_all, phone));
    }

    [Fact]
    public async Task AnSmsOutage_KeepsItsHistoricalErrorCode()
    {
        await _all.PrepareDatabaseAsync();
        _all.Sms.Fail = true;

        var send = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = GermanPhone(), channel = "Sms" });

        Assert.Equal("SMS_FAILED", Code(send.Json));
    }

    [Fact]
    public async Task AnIneligibleNumber_OnTelegram_GetsADecoy_AndNothingIsSent()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();

        // password reset for a number with no account
        var send = await Post(_all, "/api/auth/phone/password-reset/send-otp", new { phoneNumber = phone, channel = "Telegram" });

        Assert.Equal(HttpStatusCode.OK, send.Status);
        Assert.Equal(0, _all.Telegram.SendCount);
        Assert.Equal(OtpChannel.Telegram, (await Stored(_all, Challenge(send.Json))).Channel);
    }

    // ───────────── one limit for every channel ─────────────

    [Fact]
    public async Task TheHourlyLimit_IsSharedByEveryChannel_SwitchingChannelDoesNotResetIt()
    {
        await _all.PrepareDatabaseAsync();
        var phone = GermanPhone();

        var one = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Sms" });
        var two = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });
        var three = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "WhatsApp" });
        var four = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Telegram" });
        var five = await Post(_all, "/api/auth/phone/registration/send-otp", new { phoneNumber = phone, channel = "Sms" });

        Assert.Equal(1, _all.Sms.SendCount);
        Assert.Equal(1, _all.Telegram.SendCount);
        Assert.Equal(1, _all.WhatsApp.SendCount);
        Assert.Equal(3, await StoredCount(_all, phone));
        // past three, every answer is the most recent challenge, whatever channel was asked for
        Assert.Equal(Challenge(three.Json), Challenge(four.Json));
        Assert.Equal(Challenge(three.Json), Challenge(five.Json));
        Assert.NotEqual(Challenge(one.Json), Challenge(two.Json));
    }

    // Everything the caller can see about a send answer, minus the (random) challenge id.
    private static string Shape(JsonElement json) =>
        string.Join(",", json.EnumerateObject().Where(p => p.Name != "challengeId").Select(p => $"{p.Name}={p.Value}"));

    public void Dispose()
    {
        _smsOnly.Dispose();
        _all.Dispose();
    }
}
