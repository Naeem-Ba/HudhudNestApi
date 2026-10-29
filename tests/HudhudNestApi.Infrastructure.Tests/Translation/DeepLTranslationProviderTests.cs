using System.Net;
using System.Text;
using HudhudNestApi.Application.Listings.Translations;
using HudhudNestApi.Infrastructure.Translation;
using Xunit;

namespace HudhudNestApi.Infrastructure.Tests.Translation;

public sealed class DeepLTranslationProviderTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(Uri? Uri, string? Auth, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri, request.Headers.TryGetValues("Authorization", out var auth) ? auth.FirstOrDefault() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return await respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static DeepLTranslationProvider Make(Handler handler, string key = "abc:fx") =>
        new(new Factory(handler), new DeepLOptions { ApiKey = key });

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact(DisplayName = "Sends one batched request to the free endpoint and maps the answer in order")]
    public async Task Success()
    {
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"translations":[{"detected_source_language":"AR","text":"One"},{"detected_source_language":"AR","text":"Two"}]}""")));

        var result = await Make(handler).TranslateAsync(["واحد", "اثنان"], "ar", "en", CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(["One", "Two"], result.Items!.Select(i => i.Text));
        Assert.Equal("ar", result.Items![0].DetectedSourceLanguage);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://api-free.deepl.com/v2/translate", sent.Uri!.ToString());
        Assert.Equal("DeepL-Auth-Key abc:fx", sent.Auth);
        Assert.Contains("\"target_lang\":\"EN-US\"", sent.Body);
        Assert.Contains("\"source_lang\":\"AR\"", sent.Body);
    }

    [Fact(DisplayName = "Without a source hint the vendor detects it; a Pro key uses the Pro endpoint")]
    public async Task AutoDetectAndProEndpoint()
    {
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, """{"translations":[{"detected_source_language":"EN","text":"x"}]}""")));
        await Make(handler, "prokey").TranslateAsync(["hello"], null, "de", CancellationToken.None);
        var sent = Assert.Single(handler.Requests);
        Assert.StartsWith("https://api.deepl.com/", sent.Uri!.ToString());
        Assert.DoesNotContain("source_lang", sent.Body);
        Assert.Contains("\"target_lang\":\"DE\"", sent.Body);
    }

    [Theory(DisplayName = "HTTP outcomes map to quota / transient / permanent failures")]
    [InlineData(456, TranslationFailureKind.QuotaExceeded)]
    [InlineData(429, TranslationFailureKind.Transient)]
    [InlineData(503, TranslationFailureKind.Transient)]
    [InlineData(403, TranslationFailureKind.Permanent)]
    [InlineData(400, TranslationFailureKind.Permanent)]
    public async Task Failures(int status, TranslationFailureKind expected)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var result = await Make(handler).TranslateAsync(["x"], "ar", "en", CancellationToken.None);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.Items);
    }

    [Fact(DisplayName = "Network errors are transient, a cancelled call is a time-out, malformed or short answers are permanent")]
    public async Task OtherFailures()
    {
        var net = new Handler(_ => throw new HttpRequestException("boom"));
        Assert.Equal(TranslationFailureKind.Transient, (await Make(net).TranslateAsync(["x"], "ar", "en", CancellationToken.None)).Failure);

        using var cts = new CancellationTokenSource();
        var slow = new Handler(async _ => { cts.Cancel(); await Task.Delay(5000, cts.Token); return new HttpResponseMessage(); });
        Assert.Equal(TranslationFailureKind.Timeout, (await Make(slow).TranslateAsync(["x"], "ar", "en", cts.Token)).Failure);

        var bad = new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "not json")));
        Assert.Equal(TranslationFailureKind.Permanent, (await Make(bad).TranslateAsync(["x"], "ar", "en", CancellationToken.None)).Failure);

        var short_ = new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, """{"translations":[]}""")));
        Assert.Equal(TranslationFailureKind.Permanent, (await Make(short_).TranslateAsync(["x"], "ar", "en", CancellationToken.None)).Failure);
    }

    [Fact(DisplayName = "No key means unavailable and no request is made; unsupported target is refused locally")]
    public async Task NoKey()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var provider = Make(handler, key: "");
        Assert.False(provider.IsAvailable);
        Assert.Equal(TranslationFailureKind.Permanent, (await provider.TranslateAsync(["x"], "ar", "en", CancellationToken.None)).Failure);
        Assert.Equal(TranslationFailureKind.Permanent, (await Make(handler).TranslateAsync(["x"], "ar", "fr", CancellationToken.None)).Failure);
        Assert.Empty(handler.Requests);
    }
}
