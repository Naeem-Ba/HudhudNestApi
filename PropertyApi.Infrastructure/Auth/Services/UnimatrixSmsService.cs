using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Infrastructure.Auth.Services;

/// <summary>
/// Unimatrix — SmsProvider:Provider = "Unimatrix". Lists Syriatel, MTN and Syrian Telecom.
/// SmsProvider:ApiKey is the AccessKey ID, which Unimatrix takes as a URL query parameter (its "simple mode"),
/// so this adapter is registered without HttpClient request logging: the default logger would otherwise write
/// the full URL, key included, at Information level. SmsProvider:FromNumber is the optional signature
/// (2–16 characters). ApiUrl defaults to https://api.unimtx.com/.
/// Success is HTTP 2xx AND a body of {"code":"0"}; Unimatrix reports failures such as insufficient funds
/// with a non-zero code.
/// </summary>
public sealed class UnimatrixSmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _signature;
    private readonly TimeSpan _timeout;
    private readonly ILogger<UnimatrixSmsService> _logger;

    public UnimatrixSmsService(HttpClient httpClient, IOptions<SmsProviderOptions> options, ILogger<UnimatrixSmsService> logger)
    {
        var value = options.Value;
        _httpClient = httpClient;
        var baseUrl = value.ResolveApiUrl();
        var separator = baseUrl.Contains('?') ? "&" : "?";
        _endpoint = $"{baseUrl}{separator}action=sms.message.send&accessKeyId={Uri.EscapeDataString(value.ApiKey)}";
        _signature = value.FromNumber;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(value.TimeoutSeconds, 1, 60));
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken ct = default)
    {
        var text = $"رمز التحقق: {otp} — صالح 5 دقائق";
        object payload = string.IsNullOrWhiteSpace(_signature)
            ? new { to = phoneNumber, text }
            : new { to = phoneNumber, text, signature = _signature };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_endpoint, payload, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var providerCode = ReadCode(body);

            if (response.IsSuccessStatusCode && providerCode == "0")
                return true;

            _logger.LogWarning(
                "Unimatrix SMS provider refused the message for {Phone}: status {StatusCode}, code {Code}.",
                PiiMasking.MaskPhone(phoneNumber), (int)response.StatusCode, providerCode ?? "none");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                ct.IsCancellationRequested
                    ? "Unimatrix SMS sending was cancelled for {Phone}."
                    : "Unimatrix SMS provider did not answer within the timeout for {Phone}.",
                PiiMasking.MaskPhone(phoneNumber));
            return false;
        }
        catch (Exception ex)
        {
            // The exception text of a failed request can carry the request URL (and with it the key).
            _logger.LogError("Unimatrix SMS failed for {Phone}: {ErrorType}.", PiiMasking.MaskPhone(phoneNumber), ex.GetType().Name);
            return false;
        }
    }

    private static string? ReadCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("code", out var code)
                ? code.ValueKind == JsonValueKind.String ? code.GetString() : code.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
