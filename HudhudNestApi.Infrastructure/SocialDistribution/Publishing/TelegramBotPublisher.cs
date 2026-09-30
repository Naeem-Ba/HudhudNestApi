using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// Real Telegram Bot API adapter (Phase 2 — <c>https://core.telegram.org/bots/api</c>), replacing
/// the placeholder <see cref="TelegramPublisher"/> for live traffic (both stay registered — see
/// <see cref="SocialDistributionInfrastructureRegistration"/>; <see cref="Publishing.SocialPublisherRegistry"/>
/// resolves last-registration-wins). Deliberately NOT a subclass of
/// <see cref="PlatformNotConfiguredPublisherBase"/> — see that class's own remarks for why a real
/// adapter shares nothing with a placeholder except the platform-agnostic
/// <see cref="SocialContentValidator"/> policy check, which this class calls directly.
///
/// <see cref="Domain.SocialDistribution.Entities.SocialAccount.ExternalAccountId"/> is the chat_id
/// the Bot API expects — either the channel's numeric id (e.g. "-1001234567890") or, for a public
/// channel, "@handle". <see cref="Domain.SocialDistribution.Entities.SocialAccount.CredentialReference"/>
/// (Phase 3 — per-account credential store, spec §D3) is now the real bot token for that specific
/// account when the operator has connected one via <c>POST accounts/{id}/connect</c> — see
/// <see cref="ResolveToken"/>. <see cref="TelegramBotOptions.BotToken"/> is only the FALLBACK used
/// when an account has no credential of its own, preserving the original "one shared bot for every
/// account" MVP behavior for any account that was never individually connected.
///
/// Idempotency limitation (spec/§F-8 follow-up, inherent to the Bot API, which has no
/// idempotency-key concept): if our own client times out AFTER Telegram already processed the
/// call, a retry can genuinely double-post. <see cref="SocialPublication.ReleaseExpiredLease"/>
/// already refuses to auto-retry an unconfirmed (crashed-worker) attempt for exactly this reason;
/// a plain client-side timeout on an otherwise-healthy worker is the one case still not fully
/// closed — same documented residual risk as the lease mechanism's own remarks.
///
/// Written and tested (below) against a mocked HttpMessageHandler reproducing the Bot API's
/// documented request/response shapes from memory (knowledge cutoff January 2026) — same caveat
/// this codebase's own <see cref="Auth.Otp.TelegramGatewayOtpProvider"/> already carries for its
/// (different) Telegram product: it has NOT yet been run against a real bot/channel. Verify with a
/// real bot token + test channel before trusting this in production — in particular
/// <c>reply_parameters</c> (Bot API 7.0+'s replacement for the deprecated top-level
/// <c>reply_to_message_id</c>, used by <see cref="CommentAsync"/>) and the exact wording of the
/// "chat not found" 400 <see cref="ClassifyApiError"/> keys off.
/// </summary>
public sealed class TelegramBotPublisher : ISocialPublisher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelegramBotOptions _options;
    private readonly TimeSpan _timeout;
    private readonly ILogger<TelegramBotPublisher> _logger;

    /// <summary>Named client registered in <see cref="SocialDistributionInfrastructureRegistration"/> — a named (not typed) client because this adapter must stay resolvable as a Singleton <see cref="ISocialPublisher"/> alongside every other platform (see registration remarks).</summary>
    public const string HttpClientName = "TelegramBot";

    public SocialPlatform Platform => SocialPlatform.Telegram;

    public TelegramBotPublisher(IHttpClientFactory httpClientFactory, IOptions<TelegramBotOptions> options, ILogger<TelegramBotPublisher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 60));
        _logger = logger;
    }

    public SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: true,
        SupportsVideo: true,
        SupportsStories: false,
        SupportsHashtags: true,
        SupportsScheduling: false,
        SupportsUpdate: true,
        SupportsDelete: true,
        MaxTextLength: 100,
        MaxImages: 10,
        RequiresImage: false,
        // Real Bot API constraint: sendPhoto's caption tops out at 1024 characters, well under
        // sendMessage's 4096 (SocialContentPolicy's generic MaxBodyLength for this platform).
        MaxCaptionLengthWithImage: 1024);

    public SocialContentValidationResult ValidateContent(SocialPublishRequest request) =>
        SocialContentValidator.Validate(request, Platform, GetCapabilities());

    public async Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
    {
        var text = BuildMessageText(request);
        var method = string.IsNullOrEmpty(request.ImageUrl) ? "sendMessage" : "sendPhoto";
        object payload = string.IsNullOrEmpty(request.ImageUrl)
            ? new { chat_id = request.ExternalAccountId, text }
            : new { chat_id = request.ExternalAccountId, photo = request.ImageUrl, caption = text };

        var outcome = await CallAsync(method, payload, ResolveToken(request), ct);
        if (!outcome.IsOk)
            return ToFailure(outcome, "نشر المنشور");

        var messageId = outcome.Result.GetProperty("message_id").GetInt32().ToString();
        return SocialPublishResult.Success(messageId, BuildPublicUrl(request.ExternalAccountId, messageId));
    }

    public async Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalPostId, out var messageId))
            return InvalidExternalPostId(externalPostId);

        // editMessageCaption only applies to a message that carries a caption (i.e. was sent via
        // sendPhoto) — every automatic publication attaches an image in practice, so this is the
        // real edit path; a genuinely caption-less message would come back as Telegram's own 400
        // ("message can't be edited"), classified like any other InvalidContent below.
        var payload = new { chat_id = request.ExternalAccountId, message_id = messageId, caption = BuildMessageText(request) };

        var outcome = await CallAsync("editMessageCaption", payload, ResolveToken(request), ct);
        return outcome.IsOk ? SocialPublishResult.Success(externalPostId) : ToFailure(outcome, "تعديل المنشور");
    }

    public async Task<SocialPublishResult> CommentAsync(SocialPublishRequest request, string externalPostId, string commentBody, CancellationToken ct = default)
    {
        if (!int.TryParse(externalPostId, out var replyToMessageId))
            return InvalidExternalPostId(externalPostId);

        var payload = new
        {
            chat_id = request.ExternalAccountId,
            text = SocialContentPolicy.SanitizePlainText(commentBody),
            reply_parameters = new { message_id = replyToMessageId },
        };

        var outcome = await CallAsync("sendMessage", payload, ResolveToken(request), ct);
        if (!outcome.IsOk)
            return ToFailure(outcome, "التعليق على المنشور");

        return SocialPublishResult.Success(outcome.Result.GetProperty("message_id").GetInt32().ToString());
    }

    public async Task<SocialPublishResult> DeleteAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalPostId, out var messageId))
            return InvalidExternalPostId(externalPostId);

        var payload = new { chat_id = request.ExternalAccountId, message_id = messageId };

        var outcome = await CallAsync("deleteMessage", payload, ResolveToken(request), ct);
        return outcome.IsOk ? SocialPublishResult.Success(externalPostId) : ToFailure(outcome, "حذف المنشور");
    }

    /// <summary>Caption/text = body, plus hashtags appended only when there is room left under whichever length ceiling applies — never truncates the body itself to make room.</summary>
    private string BuildMessageText(SocialPublishRequest request)
    {
        if (request.Hashtags.Count == 0)
            return request.Body;

        var limit = string.IsNullOrEmpty(request.ImageUrl)
            ? SocialContentPolicy.GetLimits(Platform).MaxBodyLength
            : GetCapabilities().MaxCaptionLengthWithImage ?? SocialContentPolicy.GetLimits(Platform).MaxBodyLength;

        var hashtagLine = string.Join(' ', request.Hashtags.Select(tag => $"#{tag}"));
        var withHashtags = $"{request.Body}\n\n{hashtagLine}";

        return withHashtags.Length <= limit ? withHashtags : request.Body;
    }

    /// <summary>A message link only exists for a public channel (chat_id given as "@handle") — a numeric chat_id (private channel/group) has no public URL.</summary>
    private static string? BuildPublicUrl(string externalAccountId, string messageId) =>
        externalAccountId.StartsWith('@') ? $"https://t.me/{externalAccountId[1..]}/{messageId}" : null;

    private static SocialPublishResult InvalidExternalPostId(string externalPostId) =>
        SocialPublishResult.Failure(SocialPublicationErrorCode.InvalidContent, $"معرّف المنشور الخارجي '{externalPostId}' ليس رقماً صالحاً لمعرّف رسالة تيليغرام.");

    private SocialPublishResult ToFailure(TelegramCallOutcome outcome, string actionAr)
    {
        var (code, message) = outcome.Kind switch
        {
            TelegramFailureKind.Timeout => (SocialPublicationErrorCode.Timeout, $"انتهت مهلة الاتصال بتيليغرام أثناء {actionAr}."),
            TelegramFailureKind.TransportFailure => (SocialPublicationErrorCode.NetworkError, $"تعذّر الاتصال بخوادم تيليغرام أثناء {actionAr}."),
            TelegramFailureKind.Unparseable => (SocialPublicationErrorCode.NetworkError, $"استجابة غير مفهومة من تيليغرام أثناء {actionAr}."),
            TelegramFailureKind.ApiError => ClassifyApiError(outcome, actionAr),
            _ => (SocialPublicationErrorCode.NetworkError, $"فشل غير متوقع أثناء {actionAr}."),
        };

        return SocialPublishResult.Failure(code, message);
    }

    private static (SocialPublicationErrorCode, string) ClassifyApiError(TelegramCallOutcome outcome, string actionAr)
    {
        var description = outcome.Description ?? "بلا وصف";

        return outcome.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.NotFound =>
                (SocialPublicationErrorCode.InvalidCredentials, $"رمز البوت (Bot Token) غير صالح أو مرفوض من تيليغرام أثناء {actionAr}: {description}"),
            HttpStatusCode.Forbidden =>
                (SocialPublicationErrorCode.PermissionDenied, $"البوت لا يملك صلاحية {actionAr} على هذه القناة (حُظر أو أُزيل من المشرفين): {description}"),
            HttpStatusCode.TooManyRequests =>
                (SocialPublicationErrorCode.RateLimited, BuildRateLimitMessage(outcome, actionAr, description)),
            (HttpStatusCode)400 when description.Contains("chat not found", StringComparison.OrdinalIgnoreCase) =>
                (SocialPublicationErrorCode.PermissionDenied, $"القناة المستهدفة (chat_id) غير موجودة أو لم يُضَف البوت إليها: {description}"),
            (HttpStatusCode)400 =>
                (SocialPublicationErrorCode.InvalidContent, $"طلب مرفوض من تيليغرام أثناء {actionAr}: {description}"),
            >= HttpStatusCode.InternalServerError =>
                (SocialPublicationErrorCode.ServiceUnavailable, $"خدمة تيليغرام غير متاحة حالياً أثناء {actionAr} (HTTP {(int?)outcome.StatusCode}): {description}"),
            _ =>
                (SocialPublicationErrorCode.InvalidContent, $"رفض تيليغرام {actionAr} (HTTP {(int?)outcome.StatusCode}): {description}"),
        };
    }

    private static string BuildRateLimitMessage(TelegramCallOutcome outcome, string actionAr, string description) =>
        outcome.RetryAfterSeconds is { } seconds
            ? $"تجاوز حد معدّل الطلبات لتيليغرام أثناء {actionAr} — أعد المحاولة بعد {seconds} ثانية: {description}"
            : $"تجاوز حد معدّل الطلبات لتيليغرام أثناء {actionAr}: {description}";

    /// <summary>Phase 3: prefer the account's own connected credential over the shared fallback — see class remarks.</summary>
    private string ResolveToken(SocialPublishRequest request) =>
        string.IsNullOrWhiteSpace(request.CredentialReference) ? _options.BotToken : request.CredentialReference;

    private async Task<TelegramCallOutcome> CallAsync(string method, object payload, string botToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        // NOT the two-argument Uri(baseUri, relative) combinator: a real bot token is
        // "<numeric id>:<random string>" (e.g. "123456:ABC-DEF..."), and RFC 3986 accepts
        // "123456" as a valid scheme name — .NET's Uri combinator then parses the "relative"
        // string "bot123456:ABC.../sendPhoto" as its OWN absolute URI with scheme "bot123456",
        // silently discarding the base entirely. Building the full absolute string once and
        // parsing it in a single pass avoids that reinterpretation.
        var endpoint = new Uri($"{_options.BaseUrl.TrimEnd('/')}/bot{botToken}/{method}");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(payload) };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return TelegramCallOutcome.Parse(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Telegram Bot API did not answer within the timeout ({Method}).", method);
            return TelegramCallOutcome.Failure(TelegramFailureKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Telegram Bot API call was cancelled ({Method}).", method);
            return TelegramCallOutcome.Failure(TelegramFailureKind.Timeout);
        }
        catch (Exception ex)
        {
            // Never let a transport exception escape — the caller (PublishSocialPublicationCommandHandler)
            // treats an escaping exception as unexpected/logs it in full; an ordinary network
            // failure is expected here and must come back as a classified, retryable failure instead.
            _logger.LogError(ex, "Telegram Bot API request failed ({Method}): {ErrorType}.", method, ex.GetType().Name);
            return TelegramCallOutcome.Failure(TelegramFailureKind.TransportFailure);
        }
    }

    private enum TelegramFailureKind
    {
        None,
        Timeout,
        TransportFailure,
        Unparseable,
        ApiError,
    }

    private readonly record struct TelegramCallOutcome(
        bool IsOk, JsonElement Result, HttpStatusCode? StatusCode, string? Description, int? RetryAfterSeconds, TelegramFailureKind Kind)
    {
        public static TelegramCallOutcome Failure(TelegramFailureKind kind) => new(false, default, null, null, null, kind);

        public static TelegramCallOutcome Parse(HttpStatusCode statusCode, string body)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("ok", out var okElement) &&
                        okElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        var ok = okElement.GetBoolean() && statusCode == HttpStatusCode.OK;
                        if (ok)
                        {
                            var result = root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : default;
                            return new TelegramCallOutcome(true, result, statusCode, null, null, TelegramFailureKind.None);
                        }

                        var description = root.TryGetProperty("description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
                            ? Sanitize(descriptionElement.GetString())
                            : null;

                        int? retryAfter = null;
                        if (root.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object &&
                            parameters.TryGetProperty("retry_after", out var retryAfterElement) && retryAfterElement.ValueKind == JsonValueKind.Number)
                        {
                            retryAfter = retryAfterElement.GetInt32();
                        }

                        return new TelegramCallOutcome(false, default, statusCode, description, retryAfter, TelegramFailureKind.ApiError);
                    }
                }
                catch (JsonException)
                {
                    // Falls through to the status-code-only classification below.
                }
            }

            // No usable {"ok": ...} envelope (empty body, non-JSON, or an unexpected shape) — a
            // clearly-erroring HTTP status still tells us something real (e.g. a 502/503 from a
            // proxy or an outage in front of Telegram, which never returns Telegram's own JSON at
            // all) — classify by status code rather than defaulting every unreadable body to the
            // same generic "network error". Only a 200 OK with a garbage body is genuinely
            // ambiguous — that alone stays Unparseable.
            return statusCode == HttpStatusCode.OK
                ? new TelegramCallOutcome(false, default, statusCode, null, null, TelegramFailureKind.Unparseable)
                : new TelegramCallOutcome(false, default, statusCode, null, null, TelegramFailureKind.ApiError);
        }

        // Telegram's own error description, kept human-readable (unlike TelegramGatewayOtpProvider's
        // ASCII-only token sanitizer — this value is a display message, not a stored request id):
        // strips control characters/newlines only, and caps length before it is ever wrapped into
        // our own message or persisted (SocialPublication.ErrorMessage truncates separately too).
        private static string? Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return clean.Length == 0 ? null : (clean.Length <= 300 ? clean : clean[..300]);
        }
    }
}
