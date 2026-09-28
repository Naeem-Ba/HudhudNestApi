using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Application.Common.Security;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace PropertyApi.Infrastructure.Email;

/// <summary>
/// Sends through Resend's HTTP API instead of an SMTP conversation.
///
/// The HttpClient arrives from IHttpClientFactory (see AddEmailServices) rather than
/// being constructed here; HttpClientLifetimeTests fails the build on `new HttpClient`,
/// and the pooled handler is what keeps DNS changes visible and sockets bounded.
///
/// No retry policy, deliberately: the solution carries no Polly, and both callers of
/// this class already treat a failed send as non-fatal and log it.
/// </summary>
public sealed class ResendEmailSender
    : IdentityEmailSender, IApplicationEmailSender
{
    private const string SendPath = "emails";

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;
    private readonly ResendEmailOptions _resendOptions;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        HttpClient httpClient,
        IOptions<EmailOptions> options,
        IOptions<ResendEmailOptions> resendOptions,
        ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _resendOptions = resendOptions.Value;
        _logger = logger;
    }

    public Task SendEmailAsync(
        string email,
        string subject,
        string htmlMessage)
        => SendEmailAsync(
            new EmailMessage(email, subject, htmlMessage),
            CancellationToken.None);

    public async Task SendEmailAsync(
        EmailMessage message,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.From))
        {
            _logger.LogError("Email:From is not configured.");

            throw new InvalidOperationException(
                "Email:From is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_resendOptions.ApiKey))
        {
            _logger.LogError("Email:Resend:ApiKey is not configured.");

            throw new InvalidOperationException(
                "Email:Resend:ApiKey is not configured.");
        }

        var payload = new ResendSendRequest(
            BuildFrom(),
            [message.To],
            message.Subject,
            message.Html,
            string.IsNullOrWhiteSpace(message.Text)
                ? null
                : message.Text);

        using var request =
            new HttpRequestMessage(HttpMethod.Post, SendPath)
            {
                Content = JsonContent.Create(
                    payload,
                    options: SerializerOptions)
            };

        // Set per request rather than on the client's DefaultRequestHeaders: the typed
        // client is configured once at startup, and reading the key at send time means a
        // rotated secret takes effect without a restart.
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                _resendOptions.ApiKey);

        using var response =
            await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            // Resend's {"id": "..."} is what support and the dashboard's delivery log key
            // off, so it is the one thing worth keeping when a user says "it never came".
            _logger.LogInformation(
                "Resend accepted the message {ResendMessageId} to {Email} with subject {Subject}.",
                await ReadMessageIdAsync(response, ct),
                PiiMasking.MaskEmail(message.To),
                message.Subject);

            return;
        }

        // The body carries Resend's own {name, message}. It is worth surfacing -- it is
        // how "domain not verified" and "you may only send to your own address" are told
        // apart -- and it never contains the API key, which is only ever in the header.
        var body =
            await ReadFailureBodyAsync(response, ct);

        _logger.LogError(
            "Resend rejected the message to {Email}. Status {StatusCode}. Response: {Response}",
            PiiMasking.MaskEmail(message.To),
            (int)response.StatusCode,
            body);

        // The recipient is masked here too: this message ends up in the callers' error logs
        // and in tickets, and the address adds nothing the status and body do not say.
        throw new InvalidOperationException(
            $"Resend returned {(int)response.StatusCode} when sending to " +
            $"{PiiMasking.MaskEmail(message.To)}: {Diagnose(response.StatusCode)}{body}");
    }

    /// <summary>
    /// Points the developer at the setting that is almost certainly wrong. Only the two
    /// statuses that mean "the configuration is at fault" get a hint; everything else is
    /// left to Resend's own body.
    /// </summary>
    private static string Diagnose(HttpStatusCode status)
        => status switch
        {
            HttpStatusCode.Unauthorized =>
                "[Email:Resend:ApiKey was rejected -- it is missing, revoked or invalid.] ",

            HttpStatusCode.Forbidden =>
                "[Resend refused the sender -- check that the domain of Email:From is verified " +
                "in the Resend dashboard, and that Email:Resend:ApiKey has sending access.] ",

            _ => string.Empty
        };

    private static async Task<string> ReadMessageIdAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct),
                cancellationToken: ct);

            return document.RootElement.TryGetProperty("id", out var id) &&
                   id.ValueKind == JsonValueKind.String
                ? id.GetString()!
                : "(no id in response)";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return "(no id in response)";
        }
    }

    private string BuildFrom()
        => string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.From
            : $"{_options.FromName} <{_options.From}>";

    private static async Task<string> ReadFailureBodyAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            var body =
                await response.Content.ReadAsStringAsync(ct);

            return string.IsNullOrWhiteSpace(body)
                ? "(empty response body)"
                : body;
        }
        catch (Exception ex)
        {
            return $"(response body could not be read: {ex.GetType().Name})";
        }
    }

    private sealed record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string? Text);
}
