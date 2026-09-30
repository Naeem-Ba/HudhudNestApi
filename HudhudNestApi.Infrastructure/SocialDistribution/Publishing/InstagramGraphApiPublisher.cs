using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// Real Instagram Graph API adapter (Phase 2c — the Content Publishing API,
/// <c>https://developers.facebook.com/docs/instagram-platform/content-publishing</c>), replacing
/// the placeholder <see cref="InstagramPublisher"/> for live traffic (both stay registered — see
/// <see cref="SocialDistributionInfrastructureRegistration"/>). Shares its transport shape and
/// error-envelope parsing with <see cref="FacebookGraphApiPublisher"/> (same underlying Graph API
/// infrastructure), but the Instagram-specific publishing flow and two real, non-negotiable
/// platform limitations below make this its own class rather than a shared base.
///
/// <see cref="Domain.SocialDistribution.Entities.SocialAccount.ExternalAccountId"/> is the
/// Instagram professional (Business/Creator) account id — NOT the linked Facebook Page id (see
/// <see cref="InstagramGraphApiOptions"/>'s remarks). <see cref="Domain.SocialDistribution.Entities.SocialAccount.CredentialReference"/>
/// (Phase 3 — per-account credential store) is now the real access token for that specific
/// Instagram account when the operator has connected one — see <see cref="ResolveToken"/>.
/// <see cref="InstagramGraphApiOptions.AccessToken"/> is only the FALLBACK used when an account
/// has no credential of its own.
///
/// **Two-step publish, not one call**: Instagram has no single "create a post" endpoint. A post is
/// first created as a media *container* (<c>POST /{ig-user-id}/media</c>, returns a container id),
/// which Meta then processes asynchronously, and only THEN published
/// (<c>POST /{ig-user-id}/media_publish</c> with that container id) to actually appear on the
/// account. Publishing a container before it finishes processing fails with a documented
/// "media ID is not available" error — <see cref="PublishContainerWithRetryAsync"/> retries that
/// specific, transient case with a short delay rather than surfacing it as an ordinary failure.
///
/// **Two capabilities genuinely differ from the placeholder they replace, not by choice but by
/// platform limitation**: <see cref="GetCapabilities"/> sets <c>SupportsDelete: false</c> — the
/// Content Publishing API has no endpoint to delete a published post at all (unlike a comment,
/// which does have one) — the placeholder's <c>SupportsDelete: true</c> was aspirational, never
/// real. <c>SupportsUpdate: false</c> matches the placeholder (Instagram never supported editing a
/// caption after publish either). Both <see cref="UpdateAsync"/> and <see cref="DeleteAsync"/>
/// still honor the interface's "never throw" contract by returning a classified, non-retryable
/// failure without ever making a network call — <see cref="PropertyStatusChangedDistributionHandler"/>
/// already gates every call site on these exact capability flags, so in practice neither is
/// reachable from this codebase's only caller; they exist for any other caller that doesn't check
/// first (spec: "a safe implementation must still return a classified failure rather than throw").
///
/// Written and tested (below) against a mocked HttpMessageHandler reproducing the Graph API's
/// documented request/response shapes from memory (knowledge cutoff January 2026) — has NOT yet
/// been run against a real Instagram professional account. Verify with a real account + test post
/// before trusting this in production — in particular the exact <c>error.code</c> (documented as
/// 9007, "media ID is not available") Meta returns for a container still processing, and whether a
/// single real image container ever needs more than one publish retry in practice.
/// </summary>
public sealed class InstagramGraphApiPublisher : ISocialPublisher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly InstagramGraphApiOptions _options;
    private readonly TimeSpan _timeout;
    private readonly ILogger<InstagramGraphApiPublisher> _logger;

    /// <summary>Named client registered in <see cref="SocialDistributionInfrastructureRegistration"/> — a named (not typed) client for the same reason as <see cref="TelegramBotPublisher.HttpClientName"/>.</summary>
    public const string HttpClientName = "InstagramGraphApi";

    /// <summary>Meta's documented error code for "the container hasn't finished processing yet" — the one case <see cref="PublishContainerWithRetryAsync"/> retries instead of failing immediately.</summary>
    private const int ContainerNotReadyErrorCode = 9007;

    public SocialPlatform Platform => SocialPlatform.Instagram;

    public InstagramGraphApiPublisher(IHttpClientFactory httpClientFactory, IOptions<InstagramGraphApiOptions> options, ILogger<InstagramGraphApiPublisher> logger)
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
        SupportsStories: true,
        SupportsHashtags: true,
        SupportsScheduling: false,
        SupportsUpdate: false,
        // Real platform limitation, not a choice — see class remarks.
        SupportsDelete: false,
        MaxTextLength: 100,
        MaxImages: 10,
        RequiresImage: true);

    public SocialContentValidationResult ValidateContent(SocialPublishRequest request) =>
        SocialContentValidator.Validate(request, Platform, GetCapabilities());

    public async Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
    {
        var accessToken = ResolveToken(request);
        var containerOutcome = await CallAsync($"{request.ExternalAccountId}/media",
            new Dictionary<string, string> { ["image_url"] = request.ImageUrl, ["caption"] = BuildCaptionText(request) }, accessToken, ct);

        if (!containerOutcome.IsOk)
            return ToFailure(containerOutcome, "إنشاء حاوية الوسائط قبل النشر");

        var containerId = containerOutcome.Result.GetProperty("id").GetString()!;

        var publishOutcome = await PublishContainerWithRetryAsync(request.ExternalAccountId, containerId, accessToken, ct);
        if (!publishOutcome.IsOk)
            return ToFailure(publishOutcome, "نشر المنشور");

        var mediaId = publishOutcome.Result.GetProperty("id").GetString()!;

        // No reliable, non-fabricated public URL to return here: the media id Graph API returns
        // is not the "/p/{shortcode}/" value Instagram's own URLs use, and deriving one would mean
        // either guessing (never — spec rule 15) or an extra GET call to fetch the real
        // "permalink" field, not implemented in this MVP.
        return SocialPublishResult.Success(mediaId);
    }

    /// <summary>Placeholder-equivalent, network-free failure — see class remarks for why this is a real platform limitation, not a placeholder concern.</summary>
    public Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        _logger.LogWarning("Instagram Content Publishing API has no endpoint to edit a published post's caption — cannot update {ExternalPostId}.", externalPostId);
        return Task.FromResult(SocialPublishResult.Failure(
            SocialPublicationErrorCode.InvalidContent, "لا تدعم واجهة إنستغرام (Content Publishing API) تعديل تعليق منشور تم نشره بالفعل."));
    }

    public async Task<SocialPublishResult> CommentAsync(SocialPublishRequest request, string externalPostId, string commentBody, CancellationToken ct = default)
    {
        var outcome = await CallAsync($"{externalPostId}/comments",
            new Dictionary<string, string> { ["message"] = SocialContentPolicy.SanitizePlainText(commentBody) }, ResolveToken(request), ct);

        if (!outcome.IsOk)
            return ToFailure(outcome, "التعليق على المنشور");

        return SocialPublishResult.Success(outcome.Result.GetProperty("id").GetString()!);
    }

    /// <summary>Placeholder-equivalent, network-free failure — see class remarks for why this is a real platform limitation, not a placeholder concern.</summary>
    public Task<SocialPublishResult> DeleteAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default)
    {
        _logger.LogWarning("Instagram Content Publishing API has no endpoint to delete a published post — cannot delete {ExternalPostId}.", externalPostId);
        return Task.FromResult(SocialPublishResult.Failure(
            SocialPublicationErrorCode.InvalidContent, "لا تدعم واجهة إنستغرام (Content Publishing API) حذف منشور تم نشره بالفعل — يتطلب حذفه يدوياً من التطبيق."));
    }

    /// <summary>Caption = body, plus hashtags appended only when there is room left under the platform's body limit — never truncates the body itself to make room.</summary>
    private string BuildCaptionText(SocialPublishRequest request)
    {
        if (request.Hashtags.Count == 0)
            return request.Body;

        var limit = SocialContentPolicy.GetLimits(Platform).MaxBodyLength;
        var hashtagLine = string.Join(' ', request.Hashtags.Select(tag => $"#{tag}"));
        var withHashtags = $"{request.Body}\n\n{hashtagLine}";

        return withHashtags.Length <= limit ? withHashtags : request.Body;
    }

    private async Task<InstagramCallOutcome> PublishContainerWithRetryAsync(string igUserId, string containerId, string accessToken, CancellationToken ct)
    {
        var attempts = Math.Max(1, _options.ContainerPublishMaxAttempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var outcome = await CallAsync($"{igUserId}/media_publish", new Dictionary<string, string> { ["creation_id"] = containerId }, accessToken, ct);

            var isContainerNotReady = outcome is { IsOk: false, Kind: InstagramFailureKind.ApiError, ErrorCode: ContainerNotReadyErrorCode };
            if (!isContainerNotReady || attempt == attempts)
                return outcome;

            _logger.LogInformation("حاوية الوسائط {ContainerId} لم تنته معالجتها بعد — إعادة محاولة النشر ({Attempt}/{MaxAttempts}).", containerId, attempt, attempts);
            if (_options.ContainerPublishRetryDelayMilliseconds > 0)
                await Task.Delay(_options.ContainerPublishRetryDelayMilliseconds, ct);
        }

        // Unreachable (the loop always returns on its last iteration) but keeps the compiler happy.
        throw new InvalidOperationException("Unreachable.");
    }

    private SocialPublishResult ToFailure(InstagramCallOutcome outcome, string actionAr)
    {
        var (code, message) = outcome.Kind switch
        {
            InstagramFailureKind.Timeout => (SocialPublicationErrorCode.Timeout, $"انتهت مهلة الاتصال بإنستغرام أثناء {actionAr}."),
            InstagramFailureKind.TransportFailure => (SocialPublicationErrorCode.NetworkError, $"تعذّر الاتصال بخوادم إنستغرام أثناء {actionAr}."),
            InstagramFailureKind.Unparseable => (SocialPublicationErrorCode.NetworkError, $"استجابة غير مفهومة من إنستغرام أثناء {actionAr}."),
            InstagramFailureKind.ApiError => ClassifyApiError(outcome, actionAr),
            _ => (SocialPublicationErrorCode.NetworkError, $"فشل غير متوقع أثناء {actionAr}."),
        };

        return SocialPublishResult.Failure(code, message);
    }

    private static (SocialPublicationErrorCode, string) ClassifyApiError(InstagramCallOutcome outcome, string actionAr)
    {
        var description = outcome.ErrorMessage ?? "بلا وصف";

        return outcome.ErrorCode switch
        {
            190 => (SocialPublicationErrorCode.InvalidCredentials, $"رمز الوصول غير صالح أو منتهي الصلاحية أثناء {actionAr}: {description}"),
            10 or 200 => (SocialPublicationErrorCode.PermissionDenied, $"لا يملك تطبيقنا صلاحية {actionAr} على هذا الحساب: {description}"),
            4 or 17 or 32 or 613 => (SocialPublicationErrorCode.RateLimited, $"تجاوز حد معدّل الطلبات لإنستغرام أثناء {actionAr}: {description}"),
            ContainerNotReadyErrorCode => (SocialPublicationErrorCode.ServiceUnavailable, $"ما تزال معالجة الوسائط جارية على خوادم إنستغرام — أعد المحاولة لاحقاً أثناء {actionAr}: {description}"),
            100 => (SocialPublicationErrorCode.InvalidContent, $"طلب مرفوض من إنستغرام أثناء {actionAr}: {description}"),
            1 or 2 => (SocialPublicationErrorCode.ServiceUnavailable, $"خدمة إنستغرام غير متاحة حالياً أثناء {actionAr}: {description}"),
            _ when outcome.StatusCode >= HttpStatusCode.InternalServerError =>
                (SocialPublicationErrorCode.ServiceUnavailable, $"خدمة إنستغرام غير متاحة حالياً أثناء {actionAr} (HTTP {(int?)outcome.StatusCode}): {description}"),
            _ => (SocialPublicationErrorCode.InvalidContent, $"رفض إنستغرام {actionAr} (كود {outcome.ErrorCode?.ToString() ?? "غير معروف"}): {description}"),
        };
    }

    /// <summary>Phase 3: prefer the account's own connected credential over the shared fallback — see class remarks.</summary>
    private string ResolveToken(SocialPublishRequest request) =>
        string.IsNullOrWhiteSpace(request.CredentialReference) ? _options.AccessToken : request.CredentialReference;

    private async Task<InstagramCallOutcome> CallAsync(string path, Dictionary<string, string> fields, string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var endpoint = new Uri($"{_options.BaseUrl.TrimEnd('/')}/{_options.ApiVersion}/{path}");

        // Same rule as FacebookGraphApiPublisher.CallAsync: the token travels only in the form
        // body, never the URL/query string.
        fields["access_token"] = accessToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(fields) };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return InstagramCallOutcome.Parse(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Instagram Graph API did not answer within the timeout ({Path}).", path);
            return InstagramCallOutcome.Failure(InstagramFailureKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Instagram Graph API call was cancelled ({Path}).", path);
            return InstagramCallOutcome.Failure(InstagramFailureKind.Timeout);
        }
        catch (Exception ex)
        {
            // Never let a transport exception escape — see TelegramBotPublisher.CallAsync's identical remark.
            _logger.LogError(ex, "Instagram Graph API request failed ({Path}): {ErrorType}.", path, ex.GetType().Name);
            return InstagramCallOutcome.Failure(InstagramFailureKind.TransportFailure);
        }
    }

    private enum InstagramFailureKind
    {
        None,
        Timeout,
        TransportFailure,
        Unparseable,
        ApiError,
    }

    private readonly record struct InstagramCallOutcome(
        bool IsOk, JsonElement Result, HttpStatusCode? StatusCode, string? ErrorMessage, int? ErrorCode, InstagramFailureKind Kind)
    {
        public static InstagramCallOutcome Failure(InstagramFailureKind kind) => new(false, default, null, null, null, kind);

        public static InstagramCallOutcome Parse(HttpStatusCode statusCode, string body)
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

                        return new InstagramCallOutcome(false, default, statusCode, message, code, InstagramFailureKind.ApiError);
                    }

                    if (root.ValueKind == JsonValueKind.Object && statusCode == HttpStatusCode.OK)
                        return new InstagramCallOutcome(true, root.Clone(), statusCode, null, null, InstagramFailureKind.None);
                }
                catch (JsonException)
                {
                    // Falls through to the status-code-only classification below.
                }
            }

            // Same reasoning as FacebookGraphApiPublisher.FacebookCallOutcome.Parse: only a 200 OK
            // with a garbage body is genuinely ambiguous; any other unreadable body still has a
            // real HTTP status to classify by.
            return statusCode == HttpStatusCode.OK
                ? new InstagramCallOutcome(false, default, statusCode, null, null, InstagramFailureKind.Unparseable)
                : new InstagramCallOutcome(false, default, statusCode, null, null, InstagramFailureKind.ApiError);
        }

        private static string? Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return clean.Length == 0 ? null : (clean.Length <= 300 ? clean : clean[..300]);
        }
    }
}
