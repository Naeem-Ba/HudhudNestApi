using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

namespace HudhudNestApi.Infrastructure.Tests.SocialDistribution;

/// <summary>
/// Phase 2 — the first real <c>ISocialPublisher</c>, against a mocked <see cref="HttpMessageHandler"/>
/// reproducing the Bot API's documented request/response shapes (never the real Telegram service —
/// same "needs a real-account test before being trusted" caveat as
/// <c>OtpChannelProvidersTests</c>'s Telegram Gateway coverage, which this file's test-double
/// pattern mirrors exactly).
/// </summary>
public sealed class TelegramBotPublisherTests
{
    private const string Token = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";

    private static TelegramBotPublisher Publisher(HttpMessageHandler handler, int timeoutSeconds = 1) =>
        new(new FakeHttpClientFactory(handler), Options.Create(new TelegramBotOptions { BotToken = Token, TimeoutSeconds = timeoutSeconds }), NullLogger<TelegramBotPublisher>.Instance);

    private static SocialPublishRequest MakeRequest(
        string? imageUrl = "https://cdn.example.test/p.jpg", string body = "شقة رائعة للبيع", string chatId = "-1001234567890",
        IReadOnlyList<string>? hashtags = null, string? credentialReference = null) => new()
        {
            PublicationId = Guid.NewGuid(),
            Platform = SocialPlatform.Telegram,
            ExternalAccountId = chatId,
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
    public async Task PublishAsync_WithAnImage_CallsSendPhoto_WithTheTokenInTheUrlPathOnly()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":555}}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal("555", result.ExternalPostId);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendPhoto", handler.Uri);
        Assert.Equal("POST", handler.Method);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("-1001234567890", body.RootElement.GetProperty("chat_id").GetString());
        Assert.Equal("https://cdn.example.test/p.jpg", body.RootElement.GetProperty("photo").GetString());
        Assert.Equal("شقة رائعة للبيع", body.RootElement.GetProperty("caption").GetString());
    }

    [Fact]
    public async Task PublishAsync_WithoutAnImage_CallsSendMessage()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":10}}""");

        await Publisher(handler).PublishAsync(MakeRequest(imageUrl: null));

        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", handler.Uri);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("شقة رائعة للبيع", body.RootElement.GetProperty("text").GetString());
        Assert.False(body.RootElement.TryGetProperty("photo", out _));
    }

    [Fact]
    public async Task PublishAsync_AppendsHashtags_WhenTheyFitUnderTheCaptionLimit()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""");

        await Publisher(handler).PublishAsync(MakeRequest(hashtags: ["عقارات", "دمشق"]));

        using var body = JsonDocument.Parse(handler.Body);
        var caption = body.RootElement.GetProperty("caption").GetString()!;
        Assert.Contains("#عقارات", caption);
        Assert.Contains("#دمشق", caption);
        Assert.StartsWith("شقة رائعة للبيع", caption);
    }

    [Fact]
    public async Task PublishAsync_NeverTruncatesTheBody_ToMakeRoomForHashtags()
    {
        var longBody = new string('ب', 1020); // already close to the 1024 photo-caption ceiling
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""");

        await Publisher(handler).PublishAsync(MakeRequest(body: longBody, hashtags: ["عقارات_تيك"]));

        using var body = JsonDocument.Parse(handler.Body);
        // Hashtags would have pushed this over 1024 — the body itself must survive intact, hashtags dropped instead.
        Assert.Equal(longBody, body.RootElement.GetProperty("caption").GetString());
    }

    [Fact]
    public async Task PublishAsync_PublicChannelHandle_BuildsATMeLink()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":42}}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest(chatId: "@aqartech_damascus"));

        Assert.Equal("https://t.me/aqartech_damascus/42", result.ExternalPostUrl);
    }

    [Fact]
    public async Task PublishAsync_NumericChatId_HasNoPublicLink()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":42}}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest(chatId: "-1001234567890"));

        Assert.Null(result.ExternalPostUrl);
    }

    // ───────────── error classification ─────────────

    [Fact]
    public async Task PublishAsync_Http401_IsInvalidCredentials_NonRetryable()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, """{"ok":false,"error_code":401,"description":"Unauthorized"}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialPublicationErrorCode.InvalidCredentials, result.ErrorCode);
        Assert.False(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_Http403_IsPermissionDenied()
    {
        var handler = new RecordingHandler(HttpStatusCode.Forbidden, """{"ok":false,"error_code":403,"description":"Forbidden: bot was kicked from the channel chat"}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.PermissionDenied, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_ChatNotFound_IsPermissionDenied_NotGenericInvalidContent()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.PermissionDenied, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_OtherHttp400_IsInvalidContent()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: message caption is too long"}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.InvalidContent, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_Http429_IsRateLimited_Retryable_AndIncludesRetryAfter()
    {
        var handler = new RecordingHandler(HttpStatusCode.TooManyRequests,
            """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 30","parameters":{"retry_after":30}}""");

        var result = await Publisher(handler).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.RateLimited, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
        Assert.Contains("30", result.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_Http5xx_IsServiceUnavailable_Retryable()
    {
        var result = await Publisher(new RecordingHandler(HttpStatusCode.BadGateway, "")).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.ServiceUnavailable, result.ErrorCode);
        Assert.True(result.ErrorCode!.Value.IsRetryable());
    }

    [Fact]
    public async Task PublishAsync_UnparseableBody_IsNetworkError_Retryable_NeverThrows()
    {
        var result = await Publisher(new RecordingHandler(HttpStatusCode.OK, "not json")).PublishAsync(MakeRequest());

        Assert.Equal(SocialPublicationErrorCode.NetworkError, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_OkTrueOnAnErrorHttpStatus_IsNotTrusted()
    {
        var result = await Publisher(new RecordingHandler(HttpStatusCode.InternalServerError, """{"ok":true,"result":{"message_id":1}}"""))
            .PublishAsync(MakeRequest());

        Assert.False(result.IsSuccess);
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
    public async Task PublishAsync_NeverLeaksTheBotTokenIntoTheErrorMessage()
    {
        var result = await Publisher(new ThrowingHandler(new HttpRequestException($"failed to reach https://api.telegram.org/bot{Token}/sendPhoto"))).PublishAsync(MakeRequest());

        Assert.DoesNotContain(Token, result.ErrorMessage);
    }

    // ───────────── Update / Comment / Delete ─────────────

    [Fact]
    public async Task UpdateAsync_CallsEditMessageCaption_WithChatIdAndMessageId()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":555}}""");

        var result = await Publisher(handler).UpdateAsync(MakeRequest(), "555");

        Assert.True(result.IsSuccess);
        Assert.Equal($"https://api.telegram.org/bot{Token}/editMessageCaption", handler.Uri);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(555, body.RootElement.GetProperty("message_id").GetInt32());
        Assert.Equal("-1001234567890", body.RootElement.GetProperty("chat_id").GetString());
    }

    [Fact]
    public async Task CommentAsync_CallsSendMessage_AsAReplyToTheOriginalMessage()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":900}}""");

        var result = await Publisher(handler).CommentAsync(MakeRequest(), "555", "تم تحديث حالة هذا العقار.");

        Assert.True(result.IsSuccess);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", handler.Uri);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(555, body.RootElement.GetProperty("reply_parameters").GetProperty("message_id").GetInt32());
        Assert.Equal("تم تحديث حالة هذا العقار.", body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task DeleteAsync_CallsDeleteMessage_WithChatIdAndMessageId()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":true}""");

        var result = await Publisher(handler).DeleteAsync(MakeRequest(), "555");

        Assert.True(result.IsSuccess);
        Assert.Equal($"https://api.telegram.org/bot{Token}/deleteMessage", handler.Uri);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(555, body.RootElement.GetProperty("message_id").GetInt32());
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    public async Task UpdateCommentDelete_ANonNumericExternalPostId_IsRejectedLocally_NeverCallsTheApi(string externalPostId)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":true}""");
        var publisher = Publisher(handler);

        var updateResult = await publisher.UpdateAsync(MakeRequest(), externalPostId);
        var commentResult = await publisher.CommentAsync(MakeRequest(), externalPostId, "note");
        var deleteResult = await publisher.DeleteAsync(MakeRequest(), externalPostId);

        Assert.False(updateResult.IsSuccess);
        Assert.False(commentResult.IsSuccess);
        Assert.False(deleteResult.IsSuccess);
        Assert.Equal("", handler.Uri); // never actually called
    }

    // ───────────── Phase 3: per-account credential resolution ─────────────

    [Fact]
    public async Task PublishAsync_AccountHasItsOwnCredential_UsesThatToken_NotTheSharedFallback()
    {
        const string accountToken = "999999:own-account-token-not-the-shared-one";
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""");

        await Publisher(handler).PublishAsync(MakeRequest(credentialReference: accountToken));

        Assert.Equal($"https://api.telegram.org/bot{accountToken}/sendPhoto", handler.Uri);
        Assert.DoesNotContain(Token, handler.Uri);
    }

    [Fact]
    public async Task PublishAsync_AccountHasNoCredential_FallsBackToTheSharedOptionsToken()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""");

        await Publisher(handler).PublishAsync(MakeRequest(credentialReference: null));

        Assert.Equal($"https://api.telegram.org/bot{Token}/sendPhoto", handler.Uri);
    }

    [Fact]
    public async Task UpdateAsync_AlsoUsesTheAccountsOwnCredential()
    {
        const string accountToken = "999999:own-account-token";
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":555}}""");

        await Publisher(handler).UpdateAsync(MakeRequest(credentialReference: accountToken), "555");

        Assert.Equal($"https://api.telegram.org/bot{accountToken}/editMessageCaption", handler.Uri);
    }

    // ───────────── ValidateContent (shared policy, unchanged behavior) ─────────────

    [Fact]
    public void ValidateContent_SameLimitsAsThePlaceholder_BodyOverCaptionLimitWithImage_Fails()
    {
        var publisher = Publisher(new RecordingHandler(HttpStatusCode.OK, ""));

        var result = publisher.ValidateContent(MakeRequest(body: new string('a', 1500)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCapabilities_MatchesThePlaceholderItReplaces()
    {
        var publisher = Publisher(new RecordingHandler(HttpStatusCode.OK, ""));
        var placeholder = new TelegramPublisher(NullLogger<TelegramPublisher>.Instance);

        Assert.Equal(placeholder.GetCapabilities(), publisher.GetCapabilities());
    }

    // ───────────── test doubles ─────────────

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
