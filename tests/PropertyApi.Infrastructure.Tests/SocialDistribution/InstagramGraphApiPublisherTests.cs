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
/// Phase 2c — the third real <c>ISocialPublisher</c>, against a mocked <see cref="HttpMessageHandler"/>
/// reproducing the Instagram Content Publishing API's documented request/response shapes — never
/// the real Instagram service. Same "needs a real-account test before being trusted" caveat as
/// <c>TelegramBotPublisherTests</c>/<c>FacebookGraphApiPublisherTests</c>, whose test-double
/// pattern this file mirrors exactly.
/// </summary>
public sealed class InstagramGraphApiPublisherTests
{
    private const string Token = "IGAAG_fake_ig_access_token_1234567890";

    private static InstagramGraphApiPublisher Publisher(HttpMessageHandler handler, int timeoutSeconds = 1, int retryDelayMs = 0) =>
        new(new FakeHttpClientFactory(handler),
            Options.Create(new InstagramGraphApiOptions { AccessToken = Token, TimeoutSeconds = timeoutSeconds, ContainerPublishRetryDelayMilliseconds = retryDelayMs }),
            NullLogger<InstagramGraphApiPublisher>.Instance);

    private static SocialPublishRequest MakeRequest(
        string? imageUrl = "https://cdn.example.test/p.jpg", string body = "شقة رائعة للبيع", string igUserId = "17841400000000000", IReadOnlyList<string>? hashtags = null) => new()
        {
            PublicationId = Guid.NewGuid(),
            Platform = SocialPlatform.Instagram,
            ExternalAccountId = igUserId,
            CredentialReference = null,
            Title = "عنوان",
            Body = body,
            ImageUrl = imageUrl ?? string.Empty,
            TargetUrl = "https://realestateworld.world/properties/p1",
            Hashtags = hashtags ?? Array.Empty<string>(),
            Language = "ar",
        };

    // ───────────── PublishAsync — the two-step container-then-publish flow ─────────────

    [Fact]
    public async Task PublishAsync_CreatesAContainer_ThenPublishesIt()
    {
        var handler = new SequencedHandler(
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"container_1"}"""),
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"media_1"}"""));

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("media_1", result.ExternalPostId);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal("https://graph.facebook.com/v21.0/17841400000000000/media", handler.Calls[0].Uri);
        var containerForm = ParseForm(handler.Calls[0].Body);
        Assert.Equal("https://cdn.example.test/p.jpg", containerForm["image_url"]);
        Assert.Equal("شقة رائعة للبيع", containerForm["caption"]);
        Assert.Equal("https://graph.facebook.com/v21.0/17841400000000000/media_publish", handler.Calls[1].Uri);
        var publishForm = ParseForm(handler.Calls[1].Body);
        Assert.Equal("container_1", publishForm["creation_id"]);
    }

    [Fact]
    public async Task PublishAsync_NeverReturnsAGuessedPublicUrl()
    {
        var handler = new SequencedHandler(
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"container_1"}"""),
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"media_1"}"""));

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Null(result.ExternalPostUrl);
    }

    [Fact]
    public async Task PublishAsync_NeverPutsTheAccessTokenInTheUrl()
    {
        var handler = new SequencedHandler(
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"container_1"}"""),
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"media_1"}"""));

        await Publisher(handler).PublishAsync(MakeRequest());

        Assert.All(handler.Calls, call => Assert.DoesNotContain(Token, call.Uri));
    }

    [Fact]
    public async Task PublishAsync_ContainerCreationFails_NeverAttemptsPublish()
    {
        var handler = new SequencedHandler(new ScriptedResponse(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid image URL","type":"OAuthException","code":100}}"""));

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialPublicationErrorCode.InvalidContent, result.ErrorCode);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task PublishAsync_AppendsHashtags_WhenTheyFitUnderTheBodyLimit()
    {
        var handler = new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, """{"id":"c"}"""), new ScriptedResponse(HttpStatusCode.OK, """{"id":"m"}"""));

        await Publisher(handler).PublishAsync(MakeRequest(hashtags: ["عقارات", "دمشق"]));

        var form = ParseForm(handler.Calls[0].Body);
        Assert.Contains("#عقارات", form["caption"]);
        Assert.Contains("#دمشق", form["caption"]);
        Assert.StartsWith("شقة رائعة للبيع", form["caption"]);
    }

    [Fact]
    public async Task PublishAsync_NeverTruncatesTheBody_ToMakeRoomForHashtags()
    {
        var longBody = new string('ب', 2190); // already close to Instagram's 2200-char policy ceiling
        var handler = new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, """{"id":"c"}"""), new ScriptedResponse(HttpStatusCode.OK, """{"id":"m"}"""));

        await Publisher(handler).PublishAsync(MakeRequest(body: longBody, hashtags: ["عقارات_تيك"]));

        var form = ParseForm(handler.Calls[0].Body);
        Assert.Equal(longBody, form["caption"]);
    }

    // ───────────── container-not-ready retry ─────────────

    [Fact]
    public async Task PublishAsync_ContainerNotReady_RetriesPublish_ThenSucceeds()
    {
        var handler = new SequencedHandler(
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"container_1"}"""),
            new ScriptedResponse(HttpStatusCode.BadRequest, """{"error":{"message":"Media ID is not available","type":"OAuthException","code":9007}}"""),
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"media_1"}"""));

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("media_1", result.ExternalPostId);
        Assert.Equal(3, handler.Calls.Count); // container + 2 publish attempts
    }

    [Fact]
    public async Task PublishAsync_ContainerNeverReady_GivesUpAfterMaxAttempts_AsServiceUnavailable_Retryable()
    {
        var handler = new SequencedHandler(
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"container_1"}"""),
            new ScriptedResponse(HttpStatusCode.BadRequest, """{"error":{"message":"Media ID is not available","type":"OAuthException","code":9007}}"""),
            new ScriptedResponse(HttpStatusCode.BadRequest, """{"error":{"message":"Media ID is not available","type":"OAuthException","code":9007}}"""),
            new ScriptedResponse(HttpStatusCode.BadRequest, """{"error":{"message":"Media ID is not available","type":"OAuthException","code":9007}}"""));

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialPublicationErrorCode.ServiceUnavailable, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
        Assert.Equal(4, handler.Calls.Count); // container + 3 publish attempts (the configured max)
    }

    [Fact]
    public async Task PublishAsync_OtherPublishError_NeverRetried()
    {
        var handler = new SequencedHandler(
            new ScriptedResponse(HttpStatusCode.OK, """{"id":"container_1"}"""),
            new ScriptedResponse(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid parameter","type":"OAuthException","code":100}}"""));

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.InvalidContent, result.ErrorCode);
        Assert.Equal(2, handler.Calls.Count); // container + exactly one publish attempt, no retry
    }

    // ───────────── error classification ─────────────

    [Fact]
    public async Task PublishAsync_ErrorCode190_IsInvalidCredentials_NonRetryable()
    {
        var handler = new SequencedHandler(new ScriptedResponse(HttpStatusCode.BadRequest, """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}"""));

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.InvalidCredentials, result.ErrorCode);
        Assert.False(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_Http5xx_WithEmptyBody_IsServiceUnavailable_Retryable()
    {
        var result = await Publisher(new SequencedHandler(new ScriptedResponse(HttpStatusCode.BadGateway, ""))).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.ServiceUnavailable, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_UnparseableBodyOn200_IsNetworkError_Retryable_NeverThrows()
    {
        var result = await Publisher(new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, "not json"))).PublishAsync(MakeRequest());

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

    // ───────────── Comment ─────────────

    [Fact]
    public async Task CommentAsync_PostsToTheCommentsSubEndpoint()
    {
        var handler = new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, """{"id":"comment_1"}"""));

        var result = await Publisher(handler).CommentAsync(MakeRequest(), "media_1", "تم تحديث حالة هذا العقار.");

        Assert.True(result.IsSuccess);
        Assert.Equal("comment_1", result.ExternalPostId);
        Assert.Equal("https://graph.facebook.com/v21.0/media_1/comments", handler.Calls[0].Uri);
        var form = ParseForm(handler.Calls[0].Body);
        Assert.Equal("تم تحديث حالة هذا العقار.", form["message"]);
    }

    // ───────────── Update / Delete — real platform limitations, never a network call ─────────────

    [Fact]
    public async Task UpdateAsync_IsRejectedLocally_NeverCallsTheApi()
    {
        var handler = new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, """{"success":true}"""));

        var result = await Publisher(handler).UpdateAsync(MakeRequest(), "media_1");

        Assert.False(result.IsSuccess);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task DeleteAsync_IsRejectedLocally_NeverCallsTheApi()
    {
        var handler = new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, """{"success":true}"""));

        var result = await Publisher(handler).DeleteAsync(MakeRequest(), "media_1");

        Assert.False(result.IsSuccess);
        Assert.Empty(handler.Calls);
    }

    // ───────────── ValidateContent / GetCapabilities ─────────────

    [Fact]
    public void ValidateContent_RequiresAnImage_TextOnlyFails()
    {
        var publisher = Publisher(new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, "")));

        var result = publisher.ValidateContent(MakeRequest(imageUrl: null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCapabilities_DivergesFromThePlaceholder_OnSupportsDelete_BecauseThePlatformHasNoDeleteEndpoint()
    {
        var publisher = Publisher(new SequencedHandler(new ScriptedResponse(HttpStatusCode.OK, "")));
        var placeholder = new InstagramPublisher(NullLogger<InstagramPublisher>.Instance);

        var real = publisher.GetCapabilities();
        var placeholderCapabilities = placeholder.GetCapabilities();

        Assert.False(real.SupportsDelete);
        Assert.True(placeholderCapabilities.SupportsDelete);
        Assert.Equal(real with { SupportsDelete = placeholderCapabilities.SupportsDelete }, placeholderCapabilities);
    }

    // ───────────── test doubles ─────────────

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&').Select(pair => pair.Split('=', 2))
            .ToDictionary(kv => Decode(kv[0]), kv => Decode(kv[1]));

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", "%20"));

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record ScriptedResponse(HttpStatusCode Status, string Body);

    private sealed record RecordedCall(string Uri, string Method, string Body);

    /// <summary>Returns one scripted response per call, in order — needed here (unlike Telegram/Facebook's single-call RecordingHandler) because a publish is at least two real HTTP calls.</summary>
    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<ScriptedResponse> _responses;
        private ScriptedResponse _lastResponse;
        public List<RecordedCall> Calls { get; } = new();

        public SequencedHandler(params ScriptedResponse[] responses)
        {
            _responses = new Queue<ScriptedResponse>(responses);
            _lastResponse = responses[^1];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new RecordedCall(request.RequestUri!.ToString(), request.Method.Method, body));

            var response = _responses.Count > 0 ? _responses.Dequeue() : _lastResponse;
            return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
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
