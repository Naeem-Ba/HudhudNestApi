using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PropertyApi.Infrastructure.Auth.Services;

namespace PropertyApi.Infrastructure.Tests.Auth;

/// <summary>
/// The D7 Networks and Unimatrix adapters (the providers that reach Syria, which Twilio does not) against a
/// stub provider: the exact request each documents, what counts as success, the timeout, and that neither the
/// OTP, the credential nor the full number is ever logged.
/// </summary>
public sealed class SmsProviderAdaptersTests
{
    private const string Otp = "482913";
    private const string Secret = "provider-secret-key-123";
    private const string Phone = "+963944111222";

    private static IOptions<SmsProviderOptions> Opts(string provider, string from = "HudhudNest", string apiUrl = "", int timeout = 1, string templateId = "") =>
        Options.Create(new SmsProviderOptions { Provider = provider, ApiKey = Secret, FromNumber = from, ApiUrl = apiUrl, TimeoutSeconds = timeout, TemplateId = templateId });

    // ───────────── D7 Networks ─────────────

    [Fact]
    public async Task D7_SendsTheDocumentedRequest_WithTheKeyInABearerHeaderOnly()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"request_id":"r1","status":"accepted"}""");
        var service = new D7SmsService(new HttpClient(handler), Opts("D7"), NullLogger<D7SmsService>.Instance);

        Assert.True(await service.SendOtpAsync(Phone, Otp));

        Assert.Equal("https://api.d7networks.com/messages/v1/send", handler.Uri);
        Assert.Equal("POST", handler.Method);
        Assert.Equal($"Bearer {Secret}", handler.Authorization);
        Assert.DoesNotContain(Secret, handler.Uri);
        using var body = JsonDocument.Parse(handler.Body);
        var message = body.RootElement.GetProperty("messages")[0];
        Assert.Equal("HudhudNest", message.GetProperty("originator").GetString());
        Assert.Equal(Phone, message.GetProperty("recipients")[0].GetString());
        Assert.Equal("unicode", message.GetProperty("data_coding").GetString());
        Assert.Equal("text", message.GetProperty("msg_type").GetString());
        Assert.Contains(Otp, message.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Accepted, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.PaymentRequired, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task D7_StatusCode_DecidesSuccess(HttpStatusCode status, bool expected)
    {
        var service = new D7SmsService(new HttpClient(new RecordingHandler(status, "{}")), Opts("D7"), NullLogger<D7SmsService>.Instance);

        Assert.Equal(expected, await service.SendOtpAsync(Phone, Otp));
    }

    [Fact]
    public async Task D7_AProviderThatNeverAnswers_IsGivenUpOn()
    {
        var service = new D7SmsService(new HttpClient(new HangingHandler()), Opts("D7"), NullLogger<D7SmsService>.Instance);

        var send = service.SendOtpAsync(Phone, Otp);

        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(6))));
        Assert.False(await send);
    }

    [Fact]
    public async Task D7_CustomApiUrl_OverridesTheDefault()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var service = new D7SmsService(new HttpClient(handler), Opts("D7", apiUrl: "https://gateway.example.test/send"), NullLogger<D7SmsService>.Instance);

        await service.SendOtpAsync(Phone, Otp);

        Assert.Equal("https://gateway.example.test/send", handler.Uri);
    }

    // ───────────── Unimatrix ─────────────

    [Fact]
    public async Task Unimatrix_SendsTheDocumentedRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":"0","message":"Success","data":{"recipients":1}}""");
        var service = new UnimatrixSmsService(new HttpClient(handler), Opts("Unimatrix", from: "Hudhud"), NullLogger<UnimatrixSmsService>.Instance);

        Assert.True(await service.SendOtpAsync(Phone, Otp));

        Assert.StartsWith("https://api.unimtx.com/?action=sms.message.send&accessKeyId=", handler.Uri);
        Assert.Contains(Uri.EscapeDataString(Secret), handler.Uri);
        Assert.Equal("POST", handler.Method);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(Phone, body.RootElement.GetProperty("to").GetString());
        Assert.Equal("Hudhud", body.RootElement.GetProperty("signature").GetString());
    }

    [Fact]
    public async Task Unimatrix_SendsATemplate_NotFreeText_BecauseSyriaRejectsUnregisteredText()
    {
        // Free text to Syria was refused with 107141 (SmsTemplateNotExists) on an unverified account; the public
        // Arabic OTP template works without verification.
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":"0"}""");
        var service = new UnimatrixSmsService(new HttpClient(handler), Opts("Unimatrix"), NullLogger<UnimatrixSmsService>.Instance);

        await service.SendOtpAsync(Phone, Otp);

        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("pub_otp_ar_security", body.RootElement.GetProperty("templateId").GetString());
        Assert.Equal(Otp, body.RootElement.GetProperty("templateData").GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Unimatrix_ACustomTemplateId_OverridesTheDefault()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":"0"}""");
        var service = new UnimatrixSmsService(new HttpClient(handler), Opts("Unimatrix", templateId: "my_custom_otp"), NullLogger<UnimatrixSmsService>.Instance);

        await service.SendOtpAsync(Phone, Otp);

        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("my_custom_otp", body.RootElement.GetProperty("templateId").GetString());
    }

    [Fact]
    public async Task Unimatrix_TheSignatureIsOptional()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":"0"}""");
        var service = new UnimatrixSmsService(new HttpClient(handler), Opts("Unimatrix", from: ""), NullLogger<UnimatrixSmsService>.Instance);

        await service.SendOtpAsync(Phone, Otp);

        using var body = JsonDocument.Parse(handler.Body);
        Assert.False(body.RootElement.TryGetProperty("signature", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"code":"0","message":"Success"}""", true)]
    [InlineData(HttpStatusCode.OK, """{"code":0}""", true)]
    [InlineData(HttpStatusCode.BadRequest, """{"code":"105400","message":"InsufficientFunds"}""", false)]
    [InlineData(HttpStatusCode.OK, """{"code":"105400","message":"InsufficientFunds"}""", false)]
    [InlineData(HttpStatusCode.OK, "not json", false)]
    [InlineData(HttpStatusCode.OK, "", false)]
    [InlineData(HttpStatusCode.InternalServerError, """{"code":"0"}""", false)]
    public async Task Unimatrix_SuccessNeedsBothHttp2xx_AndProviderCodeZero(HttpStatusCode status, string body, bool expected)
    {
        var service = new UnimatrixSmsService(new HttpClient(new RecordingHandler(status, body)), Opts("Unimatrix"), NullLogger<UnimatrixSmsService>.Instance);

        Assert.Equal(expected, await service.SendOtpAsync(Phone, Otp));
    }

    [Fact]
    public async Task Unimatrix_AProviderThatNeverAnswers_IsGivenUpOn()
    {
        var service = new UnimatrixSmsService(new HttpClient(new HangingHandler()), Opts("Unimatrix"), NullLogger<UnimatrixSmsService>.Instance);

        var send = service.SendOtpAsync(Phone, Otp);

        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(6))));
        Assert.False(await send);
    }

    // ───────────── logging ─────────────

    [Fact]
    public async Task NeitherAdapter_LogsTheOtp_TheCredential_OrTheFullNumber()
    {
        var logger = new CapturingLogger();
        var d7 = new D7SmsService(new HttpClient(new RecordingHandler(HttpStatusCode.BadGateway, "{}")), Opts("D7"), new CapturingLogger<D7SmsService>(logger));
        var d7Down = new D7SmsService(new HttpClient(new ThrowingHandler(new HttpRequestException($"connect failed to https://x/?accessKeyId={Secret}"))), Opts("D7"), new CapturingLogger<D7SmsService>(logger));
        var uni = new UnimatrixSmsService(new HttpClient(new RecordingHandler(HttpStatusCode.BadRequest, """{"code":"105400","message":"InsufficientFunds"}""")), Opts("Unimatrix"), new CapturingLogger<UnimatrixSmsService>(logger));
        var uniDown = new UnimatrixSmsService(new HttpClient(new ThrowingHandler(new HttpRequestException($"connect failed to https://api.unimtx.com/?accessKeyId={Secret}"))), Opts("Unimatrix"), new CapturingLogger<UnimatrixSmsService>(logger));

        await d7.SendOtpAsync(Phone, Otp);
        await d7Down.SendOtpAsync(Phone, Otp);
        await uni.SendOtpAsync(Phone, Otp);
        await uniDown.SendOtpAsync(Phone, Otp);

        Assert.NotEmpty(logger.Lines);
        foreach (var line in logger.Lines)
        {
            Assert.DoesNotContain(Otp, line);
            Assert.DoesNotContain(Phone, line);
            // Unimatrix's URL carries the key, so its failures log the exception TYPE only.
            if (line.Contains("Unimatrix")) Assert.DoesNotContain(Secret, line);
        }
    }

    // ───────────── configuration ─────────────

    [Theory]
    [InlineData("D7", "https://api.d7networks.com/messages/v1/send")]
    [InlineData("unimatrix", "https://api.unimtx.com/")]
    [InlineData("Http", "")]
    [InlineData("Twilio", "")]
    public void ResolveApiUrl_UsesTheProviderDefault_OnlyWhenNoneIsConfigured(string provider, string expected)
    {
        Assert.Equal(expected, new SmsProviderOptions { Provider = provider }.ResolveApiUrl());
        Assert.Equal("https://custom.example.test/x", new SmsProviderOptions { Provider = provider, ApiUrl = "https://custom.example.test/x" }.ResolveApiUrl());
    }

    [Fact]
    public void Production_D7AndUnimatrix_NeedOnlyAKey_TheUrlDefaultsAreHttps()
    {
        new SmsProviderOptions { Provider = "D7", ApiKey = "k", FromNumber = "Hudhud" }.ValidateForEnvironment("Production");
        new SmsProviderOptions { Provider = "Unimatrix", ApiKey = "k" }.ValidateForEnvironment("Production");
    }

    [Fact]
    public void Production_D7_StillNeedsAKeyAndASenderId()
    {
        Assert.Throws<InvalidOperationException>(() => new SmsProviderOptions { Provider = "D7", FromNumber = "Hudhud" }.ValidateForEnvironment("Production"));
        Assert.Throws<InvalidOperationException>(() => new SmsProviderOptions { Provider = "D7", ApiKey = "k" }.ValidateForEnvironment("Production"));
    }

    [Fact]
    public void Production_ACustomUrlMustBeHttps()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SmsProviderOptions { Provider = "D7", ApiKey = "k", FromNumber = "H", ApiUrl = "http://insecure.example.test" }.ValidateForEnvironment("Production"));
    }

    [Fact]
    public void Production_Twilio_IsNotForcedToSatisfyTheHttpProviderKeys()
    {
        // Twilio is configured through Twilio:AccountSid / AuthToken / FromNumber; it used to fail startup
        // unless SmsProvider:ApiUrl, ApiKey and FromNumber were also filled in with values it never uses.
        new SmsProviderOptions { Provider = "Twilio" }.ValidateForEnvironment("Production");
    }

    [Fact]
    public void Production_TheGenericHttpProvider_KeepsItsStrictRules()
    {
        Assert.Throws<InvalidOperationException>(() => new SmsProviderOptions { Provider = "Http" }.ValidateForEnvironment("Production"));
        new SmsProviderOptions { Provider = "Http", ApiUrl = "https://sms.example.test/send", ApiKey = "k", FromNumber = "H" }.ValidateForEnvironment("Production");
    }

    // ───────────── test doubles ─────────────

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

    private sealed class CapturingLogger
    {
        public List<string> Lines { get; } = [];
    }

    private sealed class CapturingLogger<T>(CapturingLogger sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Lines.Add($"{typeof(T).Name}: {formatter(state, exception)}{(exception is null ? "" : " " + exception)}");
    }
}
