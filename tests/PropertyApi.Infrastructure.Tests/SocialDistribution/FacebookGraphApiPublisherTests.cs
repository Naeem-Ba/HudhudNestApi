using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Infrastructure.SocialDistribution.Publishing;

namespace PropertyApi.Infrastructure.Tests.SocialDistribution;

/// <summary>
/// Phase 2b — the second real <c>ISocialPublisher</c>, against a mocked <see cref="HttpMessageHandler"/>
/// reproducing the Graph API's documented request/response shapes — never the real Facebook
/// service. Same "needs a real-account test before being trusted" caveat as
/// <c>TelegramBotPublisherTests</c>, whose test-double pattern this file mirrors exactly.
/// </summary>
public sealed class FacebookGraphApiPublisherTests
{
    private const string Token = "EAAG_fake_page_access_token_1234567890";

    private static FacebookGraphApiPublisher Publisher(HttpMessageHandler handler, int timeoutSeconds = 1) =>
        new(new FakeHttpClientFactory(handler), Options.Create(new FacebookGraphApiOptions { PageAccessToken = Token, TimeoutSeconds = timeoutSeconds }), NullLogger<FacebookGraphApiPublisher>.Instance);

    private static SocialPublishRequest MakeRequest(
        string? imageUrl = "https://cdn.example.test/p.jpg", string body = "شقة رائعة للبيع", string pageId = "1234567890",
        IReadOnlyList<string>? hashtags = null, string? credentialReference = null) => new()
        {
            PublicationId = Guid.NewGuid(),
            Platform = SocialPlatform.Facebook,
            ExternalAccountId = pageId,
            CredentialReference = credentialReference,
            Title = "عنوان",
            Body = body,
            ImageUrl = imageUrl ?? string.Empty,
            TargetUrl = "https://realestateworld.world/properties/p1",
            Hashtags = hashtags ?? Array.Empty<string>(),
            Language = "ar",
        };

    // ───────────── PublishAsync ─────────────

    [Fact]
    public async Task PublishAsync_WithAnImage_CallsPhotos_AndUsesPostId()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"photo1","post_id":"1234567890_999"}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("1234567890_999", result.ExternalPostId);
        Assert.Equal("https://www.facebook.com/1234567890_999", result.ExternalPostUrl);
        Assert.Equal($"https://graph.facebook.com/v21.0/1234567890/photos", handler.Uri);
        Assert.Equal("POST", handler.Method);
        var form = ParseForm(handler.Body);
        Assert.Equal("https://cdn.example.test/p.jpg", form["url"]);
        Assert.Equal("شقة رائعة للبيع", form["caption"]);
        Assert.Equal(Token, form["access_token"]);
    }

    [Fact]
    public async Task PublishAsync_WithoutAnImage_CallsFeed_AndUsesIdDirectly()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"1234567890_888"}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest(imageUrl: null));

        Assert.True(result.IsSuccess);
        Assert.Equal("1234567890_888", result.ExternalPostId);
        Assert.Equal($"https://graph.facebook.com/v21.0/1234567890/feed", handler.Uri);
        var form = ParseForm(handler.Body);
        Assert.Equal("شقة رائعة للبيع", form["message"]);
        Assert.Equal("https://realestateworld.world/properties/p1", form["link"]);
        Assert.False(form.ContainsKey("photo"));
    }

    [Fact]
    public async Task PublishAsync_NeverPutsTheAccessTokenInTheUrl()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"1234567890_1"}""");

        await Publisher(handler).PublishAsync(MakeRequest(imageUrl: null));

        Assert.DoesNotContain(Token, handler.Uri);
    }

    [Fact]
    public async Task PublishAsync_AppendsHashtags_WhenTheyFitUnderTheBodyLimit()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"p","post_id":"1_1"}""");

        await Publisher(handler).PublishAsync(MakeRequest(hashtags: ["عقارات", "دمشق"]));

        var form = ParseForm(handler.Body);
        Assert.Contains("#عقارات", form["caption"]);
        Assert.Contains("#دمشق", form["caption"]);
        Assert.StartsWith("شقة رائعة للبيع", form["caption"]);
    }

    [Fact]
    public async Task PublishAsync_NeverTruncatesTheBody_ToMakeRoomForHashtags()
    {
        var longBody = new string('ب', 4990); // already close to Facebook's 5000-char policy ceiling
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"p","post_id":"1_1"}""");

        await Publisher(handler).PublishAsync(MakeRequest(body: longBody, hashtags: ["عقارات_تيك"]));

        var form = ParseForm(handler.Body);
        Assert.Equal(longBody, form["caption"]);
    }

    [Fact]
    public async Task PublishAsync_PhotoResponseWithoutPostId_HasNoPublicLink()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"photo_only_123"}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal("photo_only_123", result.ExternalPostId);
        Assert.Null(result.ExternalPostUrl);
    }

    // ───────────── error classification ─────────────

    [Fact]
    public async Task PublishAsync_ErrorCode190_IsInvalidCredentials_NonRetryable()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialPublicationErrorCode.InvalidCredentials, result.ErrorCode);
        Assert.False(result.ErrorCode!.Value.IsRetryable());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(200)]
    public async Task PublishAsync_PermissionErrorCodes_ArePermissionDenied(int code)
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, $"{{\"error\":{{\"message\":\"Permissions error\",\"type\":\"OAuthException\",\"code\":{code}}}}}");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.PermissionDenied, result.ErrorCode);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(17)]
    [InlineData(32)]
    [InlineData(613)]
    public async Task PublishAsync_RateLimitErrorCodes_AreRateLimited_Retryable(int code)
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, $"{{\"error\":{{\"message\":\"Too many calls\",\"type\":\"OAuthException\",\"code\":{code}}}}}");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.RateLimited, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_ErrorCode100_IsInvalidContent()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid parameter","type":"OAuthException","code":100}}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.InvalidContent, result.ErrorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PublishAsync_TransientErrorCodes_AreServiceUnavailable_Retryable(int code)
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, $"{{\"error\":{{\"message\":\"Service temporarily unavailable\",\"type\":\"OAuthException\",\"code\":{code}}}}}");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.ServiceUnavailable, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_UnrecognizedErrorCode_FallsBackToInvalidContent()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"error":{"message":"Some other error","type":"GraphMethodException","code":999}}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.InvalidContent, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_Http5xx_WithEmptyBody_IsServiceUnavailable_Retryable()
    {
        var result = await Publisher(new RecordingHandler(HttpStatusCode.BadGateway, "")).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.ServiceUnavailable, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_UnparseableBodyOn200_IsNetworkError_Retryable_NeverThrows()
    {
        var result = await Publisher(new RecordingHandler(HttpStatusCode.OK, "not json")).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.NetworkError, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_NetworkFailure_IsNetworkError_NeverThrows()
    {
        var result = await Publisher(new ThrowingHandler(new HttpRequestException("connection refused"))).PublishAsync(MakeRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialPublicationErrorCode.NetworkError, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_ATimeout_IsTimeout_Retryable_NeverThrows()
    {
        var send = Publisher(new HangingHandler()).PublishAsync(MakeRequest());

        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(6))));
        var result = await send;
        Assert.Equal(SocialPublicationErrorCode.Timeout, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_NeverLeaksThePageAccessTokenIntoTheErrorMessage()
    {
        var result = await Publisher(new ThrowingHandler(new HttpRequestException($"failed to reach https://graph.facebook.com/v21.0/1/feed?access_token={Token}"))).PublishAsync(MakeRequest());

        Assert.DoesNotContain(Token, result.ErrorMessage);
    }

    // ───────────── Update / Comment / Delete ─────────────

    [Fact]
    public async Task UpdateAsync_PostsMessage_ToThePostIdEndpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"success":true}""");

        var result = await Publisher(handler).UpdateAsync(MakeRequest(), "1234567890_999");

        Assert.True(result.IsSuccess);
        Assert.Equal($"https://graph.facebook.com/v21.0/1234567890_999", handler.Uri);
        var form = ParseForm(handler.Body);
        Assert.Equal("شقة رائعة للبيع", form["message"]);
    }

    [Fact]
    public async Task CommentAsync_PostsToTheCommentsSubEndpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"comment_1"}""");

        var result = await Publisher(handler).CommentAsync(MakeRequest(), "1234567890_999", "تم تحديث حالة هذا العقار.");

        Assert.True(result.IsSuccess);
        Assert.Equal("comment_1", result.ExternalPostId);
        Assert.Equal($"https://graph.facebook.com/v21.0/1234567890_999/comments", handler.Uri);
        var form = ParseForm(handler.Body);
        Assert.Equal("تم تحديث حالة هذا العقار.", form["message"]);
    }

    [Fact]
    public async Task DeleteAsync_UsesMethodOverride_NeverARealHttpDeleteVerb()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"success":true}""");

        var result = await Publisher(handler).DeleteAsync(MakeRequest(), "1234567890_999");

        Assert.True(result.IsSuccess);
        Assert.Equal("POST", handler.Method);
        Assert.Equal($"https://graph.facebook.com/v21.0/1234567890_999", handler.Uri);
        var form = ParseForm(handler.Body);
        Assert.Equal("delete", form["method"]);
        Assert.Equal(Token, form["access_token"]);
    }

    // ───────────── Phase 3: per-account credential resolution ─────────────

    [Fact]
    public async Task PublishAsync_AccountHasItsOwnCredential_UsesThatToken_NotTheSharedFallback()
    {
        const string accountToken = "EAAH_own_page_token_not_the_shared_one";
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"1_1"}""");

        await Publisher(handler).PublishAsync(MakeRequest(imageUrl: null, credentialReference: accountToken));

        var form = ParseForm(handler.Body);
        Assert.Equal(accountToken, form["access_token"]);
    }

    [Fact]
    public async Task PublishAsync_AccountHasNoCredential_FallsBackToTheSharedOptionsToken()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id":"1_1"}""");

        await Publisher(handler).PublishAsync(MakeRequest(imageUrl: null, credentialReference: null));

        var form = ParseForm(handler.Body);
        Assert.Equal(Token, form["access_token"]);
    }

    // ───────────── ValidateContent (shared policy, unchanged behavior) ─────────────

    [Fact]
    public void ValidateContent_SameLimitsAsThePlaceholder_BodyOverLimit_Fails()
    {
        var publisher = Publisher(new RecordingHandler(HttpStatusCode.OK, ""));

        var result = publisher.ValidateContent(MakeRequest(body: new string('a', 6000)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCapabilities_MatchesThePlaceholderItReplaces()
    {
        var publisher = Publisher(new RecordingHandler(HttpStatusCode.OK, ""));
        var placeholder = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);

        Assert.Equal(placeholder.GetCapabilities(), publisher.GetCapabilities());
    }

    // ───────────── test doubles ─────────────

    // application/x-www-form-urlencoded uses '+' for a literal space (an HTML-form convention,
    // not a URI escape) — Uri.UnescapeDataString alone would leave a decoded "+" untouched.
    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&').Select(pair => pair.Split('=', 2))
            .ToDictionary(kv => Decode(kv[0]), kv => Decode(kv[1]));

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", "%20"));

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string Uri { get; private set; } = "";
        public string Method { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.ToString();
            Method = request.Method.Method;
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
}
