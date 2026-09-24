using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Domain.Enums;
using PropertyApi.Infrastructure.Auth.Otp;

namespace PropertyApi.Infrastructure.Tests.Auth;

/// <summary>
/// Telegram Gateway and WhatsApp Cloud adapters against a stub provider (never the real service), and the
/// channel service that decides which channel is offered where. Nothing here contacts Telegram or Meta.
/// </summary>
public sealed class OtpChannelProvidersTests
{
    private const string Code = "482913";
    private const string Token = "gateway-secret-token-123";
    private const string Phone = "+4915772378923";

    private static IOptions<TelegramGatewayOptions> TelegramOpts(int timeout = 1, string sender = "") =>
        Options.Create(new TelegramGatewayOptions { ApiToken = Token, TimeoutSeconds = timeout, SenderUsername = sender });

    private static IOptions<WhatsAppCloudOptions> WhatsAppOpts(int timeout = 1) =>
        Options.Create(new WhatsAppCloudOptions
        {
            AccessToken = Token,
            PhoneNumberId = "1234567890",
            TemplateName = "hudhud_otp",
            TemplateLanguage = "ar",
            TimeoutSeconds = timeout
        });

    private static TelegramGatewayOtpProvider Telegram(HttpMessageHandler handler, ILogger<TelegramGatewayOtpProvider>? logger = null, int timeout = 1) =>
        new(new HttpClient(handler), TelegramOpts(timeout), logger ?? NullLogger<TelegramGatewayOtpProvider>.Instance);

    private static WhatsAppCloudOtpProvider WhatsApp(HttpMessageHandler handler, ILogger<WhatsAppCloudOtpProvider>? logger = null, int timeout = 1) =>
        new(new HttpClient(handler), WhatsAppOpts(timeout), logger ?? NullLogger<WhatsAppCloudOtpProvider>.Instance);

    // ───────────── Telegram Gateway ─────────────

    [Fact]
    public async Task Telegram_SendsTheDocumentedRequest_WithTheTokenInABearerHeaderOnly()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"request_id":"req_abc-123","phone_number":"+4915772378923"}}""");

        var result = await Telegram(handler).SendAsync(Phone, Code, CancellationToken.None);

        Assert.Equal(OtpSendOutcome.Sent, result.Outcome);
        Assert.Equal("req_abc-123", result.ProviderRequestId);
        Assert.Equal("https://gatewayapi.telegram.org/sendVerificationMessage", handler.Uri);
        Assert.Equal("POST", handler.Method);
        Assert.Equal($"Bearer {Token}", handler.Authorization);
        Assert.DoesNotContain(Token, handler.Uri);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(Phone, body.RootElement.GetProperty("phone_number").GetString());
        Assert.Equal(Code, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(300, body.RootElement.GetProperty("ttl").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("sender_username", out _));
    }

    [Fact]
    public async Task Telegram_SendsTheSenderUsername_WhenOneIsConfigured()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"request_id":"r"}}""");
        var provider = new TelegramGatewayOtpProvider(new HttpClient(handler), TelegramOpts(sender: "hudhudnest"), NullLogger<TelegramGatewayOtpProvider>.Instance);

        await provider.SendAsync(Phone, Code, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("hudhudnest", body.RootElement.GetProperty("sender_username").GetString());
    }

    [Fact]
    public async Task Telegram_OkFalse_WithANumberSpecificError_IsAnUnreachableRecipient()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":false,"error":"PHONE_NUMBER_NOT_OCCUPIED"}""");

        Assert.Equal(OtpSendOutcome.RecipientUnreachable, (await Telegram(handler).SendAsync(Phone, Code, CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"ok":false,"error":"ACCESS_TOKEN_INVALID"}""")]
    [InlineData(HttpStatusCode.OK, """{"ok":false,"error":"BALANCE_NOT_ENOUGH"}""")]
    [InlineData(HttpStatusCode.OK, """{"ok":false}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"ok":false,"error":"FLOOD_WAIT_30"}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"ok":false,"error":"ACCESS_TOKEN_INVALID"}""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.OK, """{"result":{}}""")]
    [InlineData(HttpStatusCode.OK, """{"ok":"yes"}""")]
    public async Task Telegram_AnythingElse_IsTheProviderBeingUnavailable(HttpStatusCode status, string body)
    {
        var result = await Telegram(new RecordingHandler(status, body)).SendAsync(Phone, Code, CancellationToken.None);

        Assert.Equal(OtpSendOutcome.ProviderUnavailable, result.Outcome);
        Assert.Null(result.ProviderRequestId);
    }

    [Fact]
    public async Task Telegram_OkTrueOnAnErrorStatus_IsNotTrusted()
    {
        var result = await Telegram(new RecordingHandler(HttpStatusCode.InternalServerError, """{"ok":true,"result":{"request_id":"r"}}"""))
            .SendAsync(Phone, Code, CancellationToken.None);

        Assert.Equal(OtpSendOutcome.ProviderUnavailable, result.Outcome);
    }

    [Fact]
    public async Task Telegram_ANetworkFailure_IsTheProviderBeingUnavailable_AndDoesNotThrow()
    {
        var result = await Telegram(new ThrowingHandler(new HttpRequestException("connection refused"))).SendAsync(Phone, Code, CancellationToken.None);

        Assert.Equal(OtpSendOutcome.ProviderUnavailable, result.Outcome);
    }

    [Fact]
    public async Task Telegram_AProviderThatNeverAnswers_TimesOut()
    {
        var send = Telegram(new HangingHandler()).SendAsync(Phone, Code, CancellationToken.None);

        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(6))));
        Assert.Equal(OtpSendOutcome.ProviderUnavailable, (await send).Outcome);
    }

    [Fact]
    public async Task Telegram_ACancelledCaller_StillGetsAnOutcome()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await Telegram(new HangingHandler()).SendAsync(Phone, Code, cancelled.Token);

        Assert.Equal(OtpSendOutcome.ProviderUnavailable, result.Outcome);
    }

    [Fact]
    public async Task Telegram_HostileProviderValues_AreReducedToASafeToken()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"request_id":"a b\nc<script>d"}}""");

        var result = await Telegram(handler).SendAsync(Phone, Code, CancellationToken.None);

        Assert.Equal("abcscriptd", result.ProviderRequestId);
    }

    [Fact]
    public async Task Telegram_NeverLogsTheCode_TheToken_OrTheFullNumber()
    {
        var sink = new LogSink();
        await Telegram(new RecordingHandler(HttpStatusCode.OK, """{"ok":false,"error":"PHONE_NUMBER_NOT_OCCUPIED"}"""), new SinkLogger<TelegramGatewayOtpProvider>(sink))
            .SendAsync(Phone, Code, CancellationToken.None);
        await Telegram(new RecordingHandler(HttpStatusCode.OK, """{"ok":false,"error":"ACCESS_TOKEN_INVALID"}"""), new SinkLogger<TelegramGatewayOtpProvider>(sink))
            .SendAsync(Phone, Code, CancellationToken.None);
        await Telegram(new RecordingHandler(HttpStatusCode.OK, "garbage"), new SinkLogger<TelegramGatewayOtpProvider>(sink))
            .SendAsync(Phone, Code, CancellationToken.None);
        await Telegram(new ThrowingHandler(new HttpRequestException($"failed https://gatewayapi.telegram.org/ token={Token} code={Code} phone={Phone}")), new SinkLogger<TelegramGatewayOtpProvider>(sink))
            .SendAsync(Phone, Code, CancellationToken.None);

        Assert.NotEmpty(sink.Lines);
        foreach (var line in sink.Lines)
        {
            Assert.DoesNotContain(Code, line);
            Assert.DoesNotContain(Token, line);
            Assert.DoesNotContain(Phone, line);
        }
    }

    // ───────────── WhatsApp Cloud API ─────────────

    [Fact]
    public async Task WhatsApp_SendsAnAuthenticationTemplate_ToTheNumberWithoutThePlus()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"messaging_product":"whatsapp","messages":[{"id":"wamid.HBgM123="}]}""");

        var result = await WhatsApp(handler).SendAsync(Phone, Code, CancellationToken.None);

        Assert.Equal(OtpSendOutcome.Sent, result.Outcome);
        Assert.Equal("wamid.HBgM123=", result.ProviderRequestId);
        Assert.Equal("https://graph.facebook.com/v21.0/1234567890/messages", handler.Uri);
        Assert.Equal($"Bearer {Token}", handler.Authorization);
        Assert.DoesNotContain(Token, handler.Uri);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("whatsapp", body.RootElement.GetProperty("messaging_product").GetString());
        Assert.Equal("4915772378923", body.RootElement.GetProperty("to").GetString());
        Assert.Equal("template", body.RootElement.GetProperty("type").GetString());
        var template = body.RootElement.GetProperty("template");
        Assert.Equal("hudhud_otp", template.GetProperty("name").GetString());
        Assert.Equal("ar", template.GetProperty("language").GetProperty("code").GetString());
        Assert.Contains(Code, template.GetProperty("components").GetRawText());
    }

    [Fact]
    public async Task WhatsApp_UndeliverableRecipient_IsUnreachable()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"error":{"message":"Message undeliverable","code":131026}}""");

        Assert.Equal(OtpSendOutcome.RecipientUnreachable, (await WhatsApp(handler).SendAsync(Phone, Code, CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid OAuth access token","code":190}}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":132001}}""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"messages":[]}""")]
    public async Task WhatsApp_AnythingElse_IsTheProviderBeingUnavailable(HttpStatusCode status, string body)
    {
        Assert.Equal(OtpSendOutcome.ProviderUnavailable, (await WhatsApp(new RecordingHandler(status, body)).SendAsync(Phone, Code, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task WhatsApp_TimeoutAndNetworkFailure_DoNotThrow_AndNeverLogSecrets()
    {
        var sink = new LogSink();
        var hanging = WhatsApp(new HangingHandler(), new SinkLogger<WhatsAppCloudOtpProvider>(sink));
        var send = hanging.SendAsync(Phone, Code, CancellationToken.None);
        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(6))));
        Assert.Equal(OtpSendOutcome.ProviderUnavailable, (await send).Outcome);

        var failing = WhatsApp(new ThrowingHandler(new HttpRequestException($"failed with {Token} {Code} {Phone}")), new SinkLogger<WhatsAppCloudOtpProvider>(sink));
        Assert.Equal(OtpSendOutcome.ProviderUnavailable, (await failing.SendAsync(Phone, Code, CancellationToken.None)).Outcome);

        Assert.NotEmpty(sink.Lines);
        foreach (var line in sink.Lines)
        {
            Assert.DoesNotContain(Code, line);
            Assert.DoesNotContain(Token, line);
            Assert.DoesNotContain(Phone, line);
        }
    }

    // ───────────── channel service: availability ─────────────

    private static OtpChannelService Service(OtpChannelOptions options, params IOtpProvider[] providers) =>
        new(Options.Create(options), providers, NullLogger<OtpChannelService>.Instance);

    private static FakeProvider Fake(OtpChannel channel, OtpSendResult? result = null) => new(channel, result ?? OtpSendResult.Sent());

    [Fact]
    public void Default_OnlySmsIsAvailable_TheOthersAreDisabled()
    {
        var service = Service(new OtpChannelOptions(), Fake(OtpChannel.Sms));

        var channels = service.Describe(Phone);

        Assert.Equal([OtpChannel.Sms, OtpChannel.Telegram, OtpChannel.WhatsApp], channels.Select(x => x.Channel));
        Assert.True(channels[0].Available);
        Assert.False(channels[1].Available);
        Assert.Equal(OtpChannelUnavailableReasons.ChannelDisabled, channels[1].Reason);
        Assert.False(channels[2].Available);
        Assert.Equal(OtpChannelUnavailableReasons.ChannelDisabled, channels[2].Reason);
    }

    [Fact]
    public void AnEnabledChannel_WithoutAProvider_IsUnavailable_ItNeverPromisesWhatItCannotSend()
    {
        var service = Service(new OtpChannelOptions { Telegram = new OtpChannelSettings { Enabled = true } }, Fake(OtpChannel.Sms));

        Assert.False(service.IsAvailable(OtpChannel.Telegram, Phone));
    }

    [Theory]
    [InlineData("+963944111222")]
    [InlineData("+5355555555")]
    [InlineData("+989121234567")]
    [InlineData("+85012345678")]
    public void WhatsApp_IsUnavailable_InTheCountriesMetDoesNotServe_ByDefault(string phone)
    {
        var service = Service(new OtpChannelOptions { WhatsApp = new OtpChannelSettings { Enabled = true } }, Fake(OtpChannel.Sms), Fake(OtpChannel.WhatsApp));

        var whatsApp = service.Describe(phone).Single(x => x.Channel == OtpChannel.WhatsApp);

        Assert.False(whatsApp.Available);
        Assert.Equal(OtpChannelUnavailableReasons.CountryNotSupported, whatsApp.Reason);
        Assert.Equal(["+963", "+53", "+98", "+850"], whatsApp.UnavailableCountryCodes);
        Assert.False(service.IsAvailable(OtpChannel.WhatsApp, phone));
    }

    [Fact]
    public void WhatsApp_IsAvailable_ElsewhereWhenEnabled()
    {
        var service = Service(new OtpChannelOptions { WhatsApp = new OtpChannelSettings { Enabled = true } }, Fake(OtpChannel.Sms), Fake(OtpChannel.WhatsApp));

        Assert.True(service.IsAvailable(OtpChannel.WhatsApp, Phone));
        Assert.True(service.IsAvailable(OtpChannel.Sms, "+963944111222"));
    }

    [Fact]
    public void ConfiguredUnavailableCountries_ReplaceTheDefault_AndApplyToAnyChannel()
    {
        var options = new OtpChannelOptions
        {
            Telegram = new OtpChannelSettings { Enabled = true, UnavailableCountryCodes = ["+7"] },
            WhatsApp = new OtpChannelSettings { Enabled = true, UnavailableCountryCodes = [] }
        };
        var service = Service(options, Fake(OtpChannel.Sms), Fake(OtpChannel.Telegram), Fake(OtpChannel.WhatsApp));

        Assert.False(service.IsAvailable(OtpChannel.Telegram, "+79001234567"));
        Assert.True(service.IsAvailable(OtpChannel.Telegram, Phone));
        Assert.True(service.IsAvailable(OtpChannel.WhatsApp, "+963944111222"));
    }

    [Fact]
    public void WithoutANumber_OnlySwitchedOffChannelsAreUnavailable_AndTheCountryListIsStillReturned()
    {
        var service = Service(new OtpChannelOptions { WhatsApp = new OtpChannelSettings { Enabled = true } }, Fake(OtpChannel.Sms), Fake(OtpChannel.WhatsApp));

        var whatsApp = service.Describe(null).Single(x => x.Channel == OtpChannel.WhatsApp);

        Assert.True(whatsApp.Available);
        Assert.Equal(4, whatsApp.UnavailableCountryCodes.Count);
    }

    [Fact]
    public void Recommended_FollowsTheLongestMatchingCountryCode_ThenTheDefault_AndOnlyWhenAvailable()
    {
        var options = new OtpChannelOptions
        {
            Telegram = new OtpChannelSettings { Enabled = true },
            WhatsApp = new OtpChannelSettings { Enabled = true },
            DefaultRecommended = "WhatsApp",
            RecommendedByCountryCode = new() { ["+963"] = "Telegram", ["+9639"] = "Sms" }
        };
        var service = Service(options, Fake(OtpChannel.Sms), Fake(OtpChannel.Telegram), Fake(OtpChannel.WhatsApp));

        Assert.Equal(OtpChannel.Sms, RecommendedFor(service, "+963944111222"));
        Assert.Equal(OtpChannel.Telegram, RecommendedFor(service, "+96311222333"));
        Assert.Equal(OtpChannel.WhatsApp, RecommendedFor(service, Phone));
        Assert.Equal(OtpChannel.WhatsApp, RecommendedFor(service, null));

        // The default points at WhatsApp, which is unavailable in Cuba: nothing is recommended rather than something unusable.
        Assert.Null(RecommendedFor(service, "+5355555555"));
    }

    [Fact]
    public void Recommended_CountryCodeKeys_MayBeWrittenWithoutThePlus_ForEnvironmentVariables()
    {
        var options = new OtpChannelOptions
        {
            Telegram = new OtpChannelSettings { Enabled = true },
            RecommendedByCountryCode = new() { ["963"] = "Telegram" }
        };
        var service = Service(options, Fake(OtpChannel.Sms), Fake(OtpChannel.Telegram));

        Assert.Equal(OtpChannel.Telegram, RecommendedFor(service, "+963944111222"));
        Assert.Null(RecommendedFor(service, Phone));
    }

    [Fact]
    public void Recommended_IgnoresAnUnknownChannelName()
    {
        var service = Service(new OtpChannelOptions { DefaultRecommended = "Carrier pigeon" }, Fake(OtpChannel.Sms));

        Assert.Null(RecommendedFor(service, Phone));
    }

    private static OtpChannel? RecommendedFor(OtpChannelService service, string? phone) =>
        service.Describe(phone).SingleOrDefault(x => x.Recommended)?.Channel;

    // ───────────── channel service: sending ─────────────

    [Fact]
    public async Task Send_UsesTheProviderOfTheRequestedChannel_Only()
    {
        var sms = Fake(OtpChannel.Sms);
        var telegram = Fake(OtpChannel.Telegram, OtpSendResult.Sent("req-1"));
        var service = Service(new OtpChannelOptions { Telegram = new OtpChannelSettings { Enabled = true } }, sms, telegram);

        var result = await service.SendAsync(OtpChannel.Telegram, Phone, Code, CancellationToken.None);

        Assert.Equal("req-1", result.ProviderRequestId);
        Assert.Equal(1, telegram.Calls);
        Assert.Equal(0, sms.Calls);
    }

    [Fact]
    public async Task Send_ToADisabledChannel_ReachesNoProvider()
    {
        var telegram = Fake(OtpChannel.Telegram);
        var service = Service(new OtpChannelOptions(), Fake(OtpChannel.Sms), telegram);

        var result = await service.SendAsync(OtpChannel.Telegram, Phone, Code, CancellationToken.None);

        Assert.Equal(OtpSendOutcome.ChannelUnavailable, result.Outcome);
        Assert.Equal(0, telegram.Calls);
    }

    [Fact]
    public async Task Send_AProviderThatThrows_BecomesUnavailable_WithoutLoggingTheExceptionText()
    {
        var sink = new LogSink();
        var throwing = new ThrowingProvider(OtpChannel.Telegram, new InvalidOperationException($"boom {Token} {Code} {Phone}"));
        var service = new OtpChannelService(
            Options.Create(new OtpChannelOptions { Telegram = new OtpChannelSettings { Enabled = true } }),
            [Fake(OtpChannel.Sms), throwing], new SinkLogger<OtpChannelService>(sink));

        var result = await service.SendAsync(OtpChannel.Telegram, Phone, Code, CancellationToken.None);

        Assert.Equal(OtpSendOutcome.ProviderUnavailable, result.Outcome);
        foreach (var line in sink.Lines)
        {
            Assert.DoesNotContain(Code, line);
            Assert.DoesNotContain(Token, line);
            Assert.DoesNotContain(Phone, line);
        }
    }

    // ───────────── test doubles ─────────────

    private sealed class FakeProvider(OtpChannel channel, OtpSendResult result) : IOtpProvider
    {
        public int Calls { get; private set; }
        public OtpChannel Channel => channel;

        public Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingProvider(OtpChannel channel, Exception exception) : IOtpProvider
    {
        public OtpChannel Channel => channel;
        public Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct) => throw exception;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string Uri { get; private set; } = "";
        public string Method { get; private set; } = "";
        public string Body { get; private set; } = "";
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.ToString();
            Method = request.Method.Method;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw exception;
    }

    private sealed class LogSink
    {
        public List<string> Lines { get; } = [];
    }

    private sealed class SinkLogger<T>(LogSink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Lines.Add($"{typeof(T).Name}: {formatter(state, exception)}{(exception is null ? "" : " " + exception)}");
    }
}
