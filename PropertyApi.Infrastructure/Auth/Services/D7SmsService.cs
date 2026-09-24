using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Infrastructure.Auth.Services;

/// <summary>
/// D7 Networks (UAE) — SmsProvider:Provider = "D7". Covers Syriatel and the other Syrian operators, unlike
/// Twilio, which no longer delivers to Syria.
/// SmsProvider:ApiKey is the D7 API token (sent as a Bearer header, never in the URL), SmsProvider:FromNumber
/// the originator (sender id). ApiUrl defaults to https://api.d7networks.com/messages/v1/send.
/// The text is sent as Unicode so the Arabic message arrives intact.
/// </summary>
public sealed class D7SmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly string _apiKey;
    private readonly string _originator;
    private readonly TimeSpan _timeout;
    private readonly ILogger<D7SmsService> _logger;

    public D7SmsService(HttpClient httpClient, IOptions<SmsProviderOptions> options, ILogger<D7SmsService> logger)
    {
        var value = options.Value;
        _httpClient = httpClient;
        _apiUrl = value.ResolveApiUrl();
        _apiKey = value.ApiKey;
        _originator = value.FromNumber;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(value.TimeoutSeconds, 1, 60));
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken ct = default)
    {
        var payload = new
        {
            messages = new[]
            {
                new
                {
                    originator = _originator,
                    recipients = new[] { phoneNumber },
                    content = $"رمز التحقق: {otp} — صالح 5 دقائق",
                    msg_type = "text",
                    data_coding = "unicode"
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "D7 SMS provider returned status code {StatusCode} for {Phone}.",
                    response.StatusCode, PiiMasking.MaskPhone(phoneNumber));
            }

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                ct.IsCancellationRequested
                    ? "D7 SMS sending was cancelled for {Phone}."
                    : "D7 SMS provider did not answer within the timeout for {Phone}.",
                PiiMasking.MaskPhone(phoneNumber));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "D7 SMS failed for {Phone}.", PiiMasking.MaskPhone(phoneNumber));
            return false;
        }
    }
}
