using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HudhudNestApi.Infrastructure.Auth.Services;

namespace HudhudNestApi.Infrastructure.Tests.Auth;

/// <summary>
/// Contract of the HTTP SMS adapter against a provider that misbehaves. The adapter runs inside the
/// send-OTP request, so a provider that never answers used to hold that request (and the person
/// waiting for a code) for the HttpClient default of 100 seconds, and a provider outage turned into a
/// pile of hanging requests. The adapter must give up after SmsProvider:TimeoutSeconds and report
/// failure, never throw, and never write the OTP or the API key to the log.
/// </summary>
public sealed class HttpSmsServiceTests
{
    private const string Otp = "482913";
    private const string ApiKey = "provider-secret-key-123";
    private const string Phone = "+963944111222";

    private static HttpSmsService Create(HttpMessageHandler handler, ILogger<HttpSmsService>? logger = null, int timeoutSeconds = 1) =>
        new(new HttpClient(handler),
            Options.Create(new SmsProviderOptions
            {
                ApiUrl = "https://sms.example.test/send",
                ApiKey = ApiKey,
                FromNumber = "HudhudNest",
                TimeoutSeconds = timeoutSeconds
            }),
            logger ?? NullLogger<HttpSmsService>.Instance);

    [Fact]
    public async Task ProviderThatNeverAnswers_IsGivenUpOn_AfterTheConfiguredTimeout()
    {
        var service = Create(new HangingHandler());

        var send = service.SendOtpAsync(Phone, Otp);
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(6)));

        Assert.Same(send, finished);
        Assert.False(await send);
    }

    [Fact]
    public async Task CallerCancellation_StillReturnsFalse_WithoutThrowing()
    {
        var service = Create(new HangingHandler(), timeoutSeconds: 30);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.False(await service.SendOtpAsync(Phone, Otp, cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Accepted, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task ProviderStatusCode_DecidesSuccess(HttpStatusCode status, bool expected)
    {
        var service = Create(new StatusHandler(status));

        Assert.Equal(expected, await service.SendOtpAsync(Phone, Otp));
    }

    [Fact]
    public async Task ConnectionFailure_ReturnsFalse_InsteadOfThrowing()
    {
        var service = Create(new ThrowingHandler(new HttpRequestException("connection refused")));

        Assert.False(await service.SendOtpAsync(Phone, Otp));
    }

    [Fact]
    public async Task Failures_NeverLogTheOtp_TheApiKey_OrTheFullPhoneNumber()
    {
        var logger = new CapturingLogger<HttpSmsService>();

        await Create(new StatusHandler(HttpStatusCode.BadGateway), logger).SendOtpAsync(Phone, Otp);
        await Create(new ThrowingHandler(new HttpRequestException("connection refused")), logger).SendOtpAsync(Phone, Otp);
        await Create(new HangingHandler(), logger).SendOtpAsync(Phone, Otp);

        Assert.NotEmpty(logger.Lines);
        foreach (var line in logger.Lines)
        {
            Assert.DoesNotContain(Otp, line);
            Assert.DoesNotContain(ApiKey, line);
            Assert.DoesNotContain(Phone, line);
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

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
