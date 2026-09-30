using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// Real Facebook Graph API adapter (Phase 2b — <c>https://developers.facebook.com/docs/graph-api</c>),
/// replacing the placeholder <see cref="FacebookPublisher"/> for live traffic (both stay
/// registered — see <see cref="SocialDistributionInfrastructureRegistration"/>;
/// <see cref="Publishing.SocialPublisherRegistry"/> resolves last-registration-wins). Deliberately
/// NOT a subclass of <see cref="PlatformNotConfiguredPublisherBase"/> — same reasoning as
/// <see cref="TelegramBotPublisher"/>'s own remarks: a real adapter shares nothing with a
/// placeholder except the platform-agnostic <see cref="SocialContentValidator"/> policy check.
///
/// <see cref="Domain.SocialDistribution.Entities.SocialAccount.ExternalAccountId"/> is the Page id
/// the Graph API expects. <see cref="Domain.SocialDistribution.Entities.SocialAccount.CredentialReference"/>
/// (Phase 3 — per-account credential store) is now the real Page access token for that specific
/// Page when the operator has connected one — see <see cref="ResolveToken"/>. This matters more
/// here than for Telegram: a Page access token is scoped to exactly one Page, so more than one
/// connected Facebook account genuinely cannot share a single token the way one Telegram bot can
/// front several channels. <see cref="FacebookGraphApiOptions.PageAccessToken"/> is only the
/// FALLBACK used when an account has no credential of its own.
///
/// Unlike the Bot API, the Graph API has no scheme-collision risk in its own endpoint path (the
/// token is never part of the URL here — see <see cref="CallAsync"/>), but it has the opposite
/// classification quirk: nearly every error — including an expired/invalid token — comes back as
/// HTTP 400 with the real signal inside the JSON <c>error.code</c> field, never in the HTTP status
/// itself. <see cref="ClassifyApiError"/> therefore keys off that field, only falling back to the
/// HTTP status when the error envelope itself can't be parsed at all.
///
/// Written and tested (below) against a mocked HttpMessageHandler reproducing the Graph API's
/// documented request/response shapes from memory (knowledge cutoff January 2026) — has NOT yet
/// been run against a real Page/App. Verify with a real Page access token + test Page before
/// trusting this in production — in particular the exact <c>error.code</c> Meta returns for a
/// revoked/expired Page token (documented as 190, but subcodes vary) and whether editing a photo
/// post's message via its <c>post_id</c> behaves identically to editing a plain feed post's.
/// </summary>
public sealed class FacebookGraphApiPublisher : ISocialPublisher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FacebookGraphApiOptions _options;
    private readonly TimeSpan _timeout;
    private readonly ILogger<FacebookGraphApiPublisher> _logger;

    /// <summary>Named client registered in <see cref="SocialDistributionInfrastructureRegistration"/> — a named (not typed) client for the same reason as <see cref="TelegramBotPublisher.HttpClientName"/>.</summary>
    public const string HttpClientName = "FacebookGraphApi";

    public SocialPlatform Platform => SocialPlatform.Facebook;

    public FacebookGraphApiPublisher(IHttpClientFactory httpClientFactory, IOptions<FacebookGraphApiOptions> options, ILogger<FacebookGraphApiPublisher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 60));
        _logger = logger;
    }

    // Same values as the placeholder FacebookPublisher this replaces — every SupportsX flag here
    // is descriptive metadata not currently enforced by SocialContentValidator (only
    // RequiresImage/MaxTextLength/MaxCaptionLengthWithImage/SupportsHashtags actually gate
    // anything today; see that class), so keeping them identical to the placeholder is not a
    // regression — Video/Stories/Scheduling are aspirational for this platform exactly as they
    // already were for the real TelegramBotPublisher (which also doesn't send video, despite
    // SupportsVideo: true).
    public SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: true,
        SupportsVideo: true,
        SupportsStories: true,
        SupportsHashtags: true,
        SupportsScheduling: true,
        SupportsUpdate: true,
        SupportsDelete: true,
        MaxTextLength: 100,
        MaxImages: 10,
        RequiresImage: false);

    public SocialContentValidationResult ValidateContent(SocialPublishRequest request) =>
        SocialContentValidator.Validate(request, Platform, GetCapabilities());

    public async Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
    {
        var text = BuildMessageText(request);

        var accessToken = ResolveToken(request);
        FacebookCallOutcome outcome;
        if (string.IsNullOrEmpty(request.ImageUrl))
        {
            outcome = await CallAsync($"{request.ExternalAccountId}/feed",
                new Dictionary<string, string> { ["message"] = text, ["link"] = request.TargetUrl }, accessToken, ct);
        }
        else
        {
            outcome = await CallAsync($"{request.ExternalAccountId}/photos",
                new Dictionary<string, string> { ["url"] = request.ImageUrl, ["caption"] = text }, accessToken, ct);
        }

        if (!outcome.IsOk)
            return ToFailure(outcome, "نشر المنشور");

        // /photos returns both "id" (the photo object) and "post_id" (the actual page post,
        // format "{page-id}_{story-id}") when published straight to the Page's timeline (the
        // default, since we never pass "no_story"/"unpublished"). /feed returns only "id", which
        // IS the post id directly. post_id is what Update/Comment/Delete must act on, so prefer it.
        var postId = outcome.Result.TryGetProperty("post_id", out var postIdElement)
            ? postIdElement.GetString()!
            : outcome.Result.GetProperty("id").GetString()!;

        return SocialPublishResult.Success(postId, BuildPublicUrl(postId));
    }

    public async Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        var outcome = await CallAsync(externalPostId, new Dictionary<string, string> { ["message"] = BuildMessageText(request) }, ResolveToken(request), ct);
        return outcome.IsOk ? SocialPublishResult.Success(externalPostId) : ToFailure(outcome, "تعديل المنشور");
    }

    public async Task<SocialPublishResult> CommentAsync(SocialPublishRequest request, string externalPostId, string commentBody, CancellationToken ct = default)
    {
        var outcome = await CallAsync($"{externalPostId}/comments",
            new Dictionary<string, string> { ["message"] = SocialContentPolicy.SanitizePlainText(commentBody) }, ResolveToken(request), ct);

        if (!outcome.IsOk)
            return ToFailure(outcome, "التعليق على المنشور");

        return SocialPublishResult.Success(outcome.Result.GetProperty("id").GetString()!);
    }

    public async Task<SocialPublishResult> DeleteAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        // Method-override (Graph API's own documented approach for DELETE via a POST body) rather
        // than an actual HTTP DELETE, purely so the access token can travel in the form body —
        // same "never in a URL/query string" rule CallAsync already follows for every other call.
        var outcome = await CallAsync(externalPostId, new Dictionary<string, string> { ["method"] = "delete" }, ResolveToken(request), ct);
        return outcome.IsOk ? SocialPublishResult.Success(externalPostId) : ToFailure(outcome, "حذف المنشور");
    }

    /// <summary>Phase 3: prefer the account's own connected credential over the shared fallback — see class remarks.</summary>
    private string ResolveToken(SocialPublishRequest request) =>
        string.IsNullOrWhiteSpace(request.CredentialReference) ? _options.PageAccessToken : request.CredentialReference;

    /// <summary>Caption/message = body, plus hashtags appended only when there is room left under the platform's body limit — never truncates the body itself to make room.</summary>
    private string BuildMessageText(SocialPublishRequest request)
    {
        if (request.Hashtags.Count == 0)
            return request.Body;

        var limit = SocialContentPolicy.GetLimits(Platform).MaxBodyLength;
        var hashtagLine = string.Join(' ', request.Hashtags.Select(tag => $"#{tag}"));
        var withHashtags = $"{request.Body}\n\n{hashtagLine}";

        return withHashtags.Length <= limit ? withHashtags : request.Body;
    }

    private static readonly Regex PageScopedPostIdPattern = new(@"^\d+_\d+$", RegexOptions.Compiled);

    /// <summary>A post URL only resolves reliably for the "{page-id}_{story-id}" post_id shape a real publish returns (both segments purely numeric) — a bare photo id (returned when /photos has no post_id) has no equivalent public link.</summary>
    private static string? BuildPublicUrl(string postId) =>
        PageScopedPostIdPattern.IsMatch(postId) ? $"https://www.facebook.com/{postId}" : null;

    private FacebookPublishResultFailureParts ToFailureParts(FacebookCallOutcome outcome, string actionAr) => outcome.Kind switch
    {
        FacebookFailureKind.Timeout => new(SocialPublicationErrorCode.Timeout, $"انتهت مهلة الاتصال بفيسبوك أثناء {actionAr}."),
        FacebookFailureKind.TransportFailure => new(SocialPublicationErrorCode.NetworkError, $"تعذّر الاتصال بخوادم فيسبوك أثناء {actionAr}."),
        FacebookFailureKind.Unparseable => new(SocialPublicationErrorCode.NetworkError, $"استجابة غير مفهومة من فيسبوك أثناء {actionAr}."),
        FacebookFailureKind.ApiError => ClassifyApiError(outcome, actionAr),
        _ => new(SocialPublicationErrorCode.NetworkError, $"فشل غير متوقع أثناء {actionAr}."),
    };

    private SocialPublishResult ToFailure(FacebookCallOutcome outcome, string actionAr)
    {
        var parts = ToFailureParts(outcome, actionAr);
        return SocialPublishResult.Failure(parts.Code, parts.Message);
    }

    private readonly record struct FacebookPublishResultFailureParts(SocialPublicationErrorCode Code, string Message);

    private static FacebookPublishResultFailureParts ClassifyApiError(FacebookCallOutcome outcome, string actionAr)
    {
        var description = outcome.ErrorMessage ?? "بلا وصف";

        return outcome.ErrorCode switch
        {
            190 => new(SocialPublicationErrorCode.InvalidCredentials, $"رمز وصول الصفحة (Page Access Token) غير صالح أو منتهي الصلاحية أثناء {actionAr}: {description}"),
            10 or 200 => new(SocialPublicationErrorCode.PermissionDenied, $"لا يملك تطبيقنا صلاحية {actionAr} على هذه الصفحة: {description}"),
            4 or 17 or 32 or 613 => new(SocialPublicationErrorCode.RateLimited, $"تجاوز حد معدّل الطلبات لفيسبوك أثناء {actionAr}: {description}"),
            1 or 2 => new(SocialPublicationErrorCode.ServiceUnavailable, $"خدمة فيسبوك غير متاحة حالياً أثناء {actionAr}: {description}"),
            100 => new(SocialPublicationErrorCode.InvalidContent, $"طلب مرفوض من فيسبوك أثناء {actionAr}: {description}"),
            _ when outcome.StatusCode >= HttpStatusCode.InternalServerError =>
                new(SocialPublicationErrorCode.ServiceUnavailable, $"خدمة فيسبوك غير متاحة حالياً أثناء {actionAr} (HTTP {(int?)outcome.StatusCode}): {description}"),
            _ => new(SocialPublicationErrorCode.InvalidContent, $"رفض فيسبوك {actionAr} (كود {outcome.ErrorCode?.ToString() ?? "غير معروف"}): {description}"),
        };
    }

    private async Task<FacebookCallOutcome> CallAsync(string path, Dictionary<string, string> fields, string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var endpoint = new Uri($"{_options.BaseUrl.TrimEnd('/')}/{_options.ApiVersion}/{path}");

        // access_token travels only in the form body (never the URL/query string, unlike the
        // common Graph API sample code that appends it as a query param) — same rule this
        // codebase applies everywhere else to secrets, and Graph API accepts it in the body for
        // every verb used here (POST, and the delete-by-POST method-override).
        fields["access_token"] = accessToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(fields) };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return FacebookCallOutcome.Parse(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Facebook Graph API did not answer within the timeout ({Path}).", path);
            return FacebookCallOutcome.Failure(FacebookFailureKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Facebook Graph API call was cancelled ({Path}).", path);
            return FacebookCallOutcome.Failure(FacebookFailureKind.Timeout);
        }
        catch (Exception ex)
        {
            // Never let a transport exception escape — see TelegramBotPublisher.CallAsync's
            // identical remark: the caller treats an escaping exception as unexpected, but an
            // ordinary network failure here is expected and must come back classified instead.
            _logger.LogError(ex, "Facebook Graph API request failed ({Path}): {ErrorType}.", path, ex.GetType().Name);
            return FacebookCallOutcome.Failure(FacebookFailureKind.TransportFailure);
        }
    }

    private enum FacebookFailureKind
    {
        None,
        Timeout,
        TransportFailure,
        Unparseable,
        ApiError,
    }

    private readonly record struct FacebookCallOutcome(
        bool IsOk, JsonElement Result, HttpStatusCode? StatusCode, string? ErrorMessage, int? ErrorCode, FacebookFailureKind Kind)
    {
        public static FacebookCallOutcome Failure(FacebookFailureKind kind) => new(false, default, null, null, null, kind);

        public static FacebookCallOutcome Parse(HttpStatusCode statusCode, string body)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    var root = document.RootElement;

                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                    {
                        var message = errorElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                            ? Sanitize(messageElement.GetString())
                            : null;

                        int? code = errorElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                            ? codeElement.GetInt32()
                            : null;

                        return new FacebookCallOutcome(false, default, statusCode, message, code, FacebookFailureKind.ApiError);
                    }

                    if (root.ValueKind == JsonValueKind.Object && statusCode == HttpStatusCode.OK)
                        return new FacebookCallOutcome(true, root.Clone(), statusCode, null, null, FacebookFailureKind.None);
                }
                catch (JsonException)
                {
                    // Falls through to the status-code-only classification below.
                }
            }

            // No usable JSON envelope (empty body, non-JSON, or an unexpected shape) — same
            // reasoning as TelegramBotPublisher.TelegramCallOutcome.Parse: a clearly-erroring HTTP
            // status still tells us something real, so only a 200 OK with a garbage body is
            // genuinely ambiguous.
            return statusCode == HttpStatusCode.OK
                ? new FacebookCallOutcome(false, default, statusCode, null, null, FacebookFailureKind.Unparseable)
                : new FacebookCallOutcome(false, default, statusCode, null, null, FacebookFailureKind.ApiError);
        }

        // Facebook's own error message, kept human-readable (unlike a stored-request-id
        // sanitizer): strips control characters/newlines only, and caps length before it is ever
        // wrapped into our own message or persisted (SocialPublication.ErrorMessage truncates separately too).
        private static string? Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return clean.Length == 0 ? null : (clean.Length <= 300 ? clean : clean[..300]);
        }
    }
}
