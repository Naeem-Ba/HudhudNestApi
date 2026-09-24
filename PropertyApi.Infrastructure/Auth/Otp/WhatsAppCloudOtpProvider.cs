using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Application.Common.Security;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Infrastructure.Auth.Otp;

/// <summary>WhatsAppCloud:* — credentials of the official WhatsApp Business Platform. Whether the channel is on is OtpChannels:WhatsApp:Enabled.</summary>
public sealed class WhatsAppCloudOptions
{
    public const string SectionName = "WhatsAppCloud";

    public string BaseUrl { get; init; } = "https://graph.facebook.com/";
    public string ApiVersion { get; init; } = "v21.0";

    /// <summary>The Graph API access token (system-user token). Sent as a Bearer header, never logged.</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>The id of the sending phone number in the WhatsApp Business account (not the number itself).</summary>
    public string PhoneNumberId { get; init; } = string.Empty;

    /// <summary>Name of the approved authentication template (category AUTHENTICATION).</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Language code the template was approved in, for example "ar" or "en_US".</summary>
    public string TemplateLanguage { get; init; } = "ar";

    public int TimeoutSeconds { get; init; } = 10;
}

/// <summary>
/// Official WhatsApp Business Platform (Cloud API) only: an approved AUTHENTICATION template carrying the code,
/// sent with <c>POST /{phone-number-id}/messages</c>. No WhatsApp Web, no unofficial gateway.
/// Meta does not allow the Business Platform to deliver to Syria, Cuba, Iran, North Korea (and sanctioned Ukrainian
/// regions), which is why those calling codes are unavailable for this channel by default.
/// NOT VERIFIED against a real Meta account: the payload follows Meta's template documentation, and error 131026
/// (message undeliverable) is treated as an unreachable recipient; everything else is provider trouble.
/// </summary>
public sealed class WhatsAppCloudOtpProvider : IOtpProvider
{
    private const int UndeliverableErrorCode = 131026;

    private readonly HttpClient _httpClient;
    private readonly WhatsAppCloudOptions _options;
    private readonly TimeSpan _timeout;
    private readonly ILogger<WhatsAppCloudOtpProvider> _logger;

    public WhatsAppCloudOtpProvider(HttpClient httpClient, IOptions<WhatsAppCloudOptions> options,
        ILogger<WhatsAppCloudOtpProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 60));
        _logger = logger;
    }

    public OtpChannel Channel => OtpChannel.WhatsApp;

    public async Task<OtpSendResult> SendAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var masked = PiiMasking.MaskPhone(phoneNumber);
        var payload = new
        {
            messaging_product = "whatsapp",
            to = phoneNumber.TrimStart('+'),
            type = "template",
            template = new
            {
                name = _options.TemplateName,
                language = new { code = _options.TemplateLanguage },
                components = new object[]
                {
                    new { type = "body", parameters = new[] { new { type = "text", text = code } } },
                    new
                    {
                        type = "button",
                        sub_type = "url",
                        index = "0",
                        parameters = new[] { new { type = "text", text = code } }
                    }
                }
            }
        };

        var endpoint = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"),
            $"{_options.ApiVersion.Trim('/')}/{Uri.EscapeDataString(_options.PhoneNumberId)}/messages");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);

        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var (messageId, errorCode) = Parse(body);

            if (response.IsSuccessStatusCode && messageId is not null)
                return OtpSendResult.Sent(messageId);

            if (errorCode == UndeliverableErrorCode)
            {
                _logger.LogWarning("WhatsApp cannot deliver to {Phone} (error {Code}).", masked, errorCode);
                return OtpSendResult.Unreachable();
            }

            _logger.LogWarning("WhatsApp Cloud API refused the request for {Phone}: HTTP {StatusCode}, error {Code}.",
                masked, (int)response.StatusCode, errorCode?.ToString() ?? "none");
            return OtpSendResult.Unavailable();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                ct.IsCancellationRequested
                    ? "WhatsApp sending was cancelled for {Phone}."
                    : "WhatsApp Cloud API did not answer within the timeout for {Phone}.",
                masked);
            return OtpSendResult.Unavailable();
        }
        catch (Exception ex)
        {
            _logger.LogError("WhatsApp Cloud API failed for {Phone}: {ErrorType}.", masked, ex.GetType().Name);
            return OtpSendResult.Unavailable();
        }
    }

    private static (string? MessageId, int? ErrorCode) Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            string? id = null;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    if (message.ValueKind == JsonValueKind.Object &&
                        message.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                    {
                        id = new string((idElement.GetString() ?? string.Empty)
                            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '=' or '.').Take(120).ToArray());
                        break;
                    }
                }
            }

            int? code = null;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsed))
                code = parsed;

            return (string.IsNullOrEmpty(id) ? null : id, code);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
