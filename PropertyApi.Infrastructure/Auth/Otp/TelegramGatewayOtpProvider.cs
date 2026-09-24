using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Application.Common.Security;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Infrastructure.Auth.Otp;

/// <summary>TelegramGateway:* — credentials and transport settings. Whether the channel is on is OtpChannels:Telegram:Enabled.</summary>
public sealed class TelegramGatewayOptions
{
    public const string SectionName = "TelegramGateway";

    public string BaseUrl { get; init; } = "https://gatewayapi.telegram.org/";

    /// <summary>The Gateway access token. Sent as a Bearer header, never in a URL, never logged.</summary>
    public string ApiToken { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Optional verified channel username the message is sent from.</summary>
    public string SenderUsername { get; init; } = string.Empty;
}

/// <summary>
/// Telegram Gateway, official API only (https://core.telegram.org/gateway/api). Our own numeric code is sent with
/// <c>sendVerificationMessage</c> and checked locally against the stored HMAC, so verification behaves exactly as
/// it does for SMS. <c>checkSendAbility</c> is deliberately not called: it is billed and would reveal whether a
/// number has Telegram.
/// Telegram documents no error catalogue; a number-specific refusal is recognised by its <c>PHONE_NUMBER_</c> or
/// <c>USER_</c> prefix and everything else (bad token, balance, flood, HTTP failures, timeouts) counts as the
/// provider being unavailable. Both classifications need a real-account test before being trusted.
/// </summary>
public sealed class TelegramGatewayOtpProvider : IOtpProvider
{
    private const int TtlSeconds = 300;

    private readonly HttpClient _httpClient;
    private readonly TelegramGatewayOptions _options;
    private readonly TimeSpan _timeout;
    private readonly ILogger<TelegramGatewayOtpProvider> _logger;

    public TelegramGatewayOtpProvider(HttpClient httpClient, IOptions<TelegramGatewayOptions> options,
        ILogger<TelegramGatewayOtpProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 60));
        _logger = logger;
    }

    public OtpChannel Channel => OtpChannel.Telegram;

    public async Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var masked = PiiMasking.MaskPhone(phoneNumber);
        var payload = new Dictionary<string, object>
        {
            ["phone_number"] = phoneNumber,
            ["code"] = code,
            ["ttl"] = TtlSeconds
        };
        if (!string.IsNullOrWhiteSpace(_options.SenderUsername))
            payload["sender_username"] = _options.SenderUsername.Trim();

        var endpoint = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), "sendVerificationMessage");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!TryParse(body, out var ok, out var error, out var requestId))
            {
                _logger.LogWarning("Telegram Gateway returned an unreadable answer (HTTP {StatusCode}) for {Phone}.",
                    (int)response.StatusCode, masked);
                return OtpSendResult.Unavailable();
            }

            if (ok && response.IsSuccessStatusCode)
                return OtpSendResult.Sent(requestId);

            if (IsRecipientSpecific(error))
            {
                _logger.LogWarning("Telegram Gateway cannot deliver to {Phone}: {Error}.", masked, error);
                return OtpSendResult.Unreachable();
            }

            _logger.LogWarning("Telegram Gateway refused the request for {Phone}: HTTP {StatusCode}, error {Error}.",
                masked, (int)response.StatusCode, error ?? "none");
            return OtpSendResult.Unavailable();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                ct.IsCancellationRequested
                    ? "Telegram Gateway sending was cancelled for {Phone}."
                    : "Telegram Gateway did not answer within the timeout for {Phone}.",
                masked);
            return OtpSendResult.Unavailable();
        }
        catch (Exception ex)
        {
            _logger.LogError("Telegram Gateway failed for {Phone}: {ErrorType}.", masked, ex.GetType().Name);
            return OtpSendResult.Unavailable();
        }
    }

    private static bool IsRecipientSpecific(string? error) =>
        error is not null &&
        (error.StartsWith("PHONE_NUMBER_", StringComparison.Ordinal) || error.StartsWith("USER_", StringComparison.Ordinal));

    private static bool TryParse(string body, out bool ok, out string? error, out string? requestId)
    {
        ok = false;
        error = null;
        requestId = null;
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("ok", out var okElement) ||
                okElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;

            ok = okElement.GetBoolean();
            if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
                error = Sanitise(errorElement.GetString());
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty("request_id", out var id) && id.ValueKind == JsonValueKind.String)
                requestId = Sanitise(id.GetString());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Values that are copied into logs and the database come from a remote party: keep them to a short token.
    private static string? Sanitise(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var clean = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(120).ToArray());
        return clean.Length == 0 ? null : clean;
    }
}
