using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HudhudNestApi.Application.Listings.Translations;

namespace HudhudNestApi.Infrastructure.Translation;

/// <summary>Config section "ListingTranslation:DeepL". The key is a server secret (env var ListingTranslation__DeepL__ApiKey); it is never logged or returned.</summary>
public sealed class DeepLOptions
{
    public const string SectionName = "ListingTranslation:DeepL";
    public string? ApiKey { get; set; }

    /// <summary>Empty = derive from the key: keys ending in ":fx" are API Free (api-free.deepl.com), others API Pro (api.deepl.com).</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// DeepL REST adapter (POST /v2/translate with several texts in one call). Maps HTTP outcomes to
/// <see cref="TranslationFailureKind"/>: 456 = quota, 429/5xx/network = transient, other 4xx = permanent.
/// Texts, keys and response bodies are never logged.
/// </summary>
public sealed class DeepLTranslationProvider : ITranslationProvider
{
    public const string HttpClientName = "DeepLTranslation";

    private readonly IHttpClientFactory _factory;
    private readonly DeepLOptions _options;

    public DeepLTranslationProvider(IHttpClientFactory factory, DeepLOptions options)
    {
        _factory = factory;
        _options = options;
    }

    public string Name => "deepl";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public static string ResolveBaseUrl(DeepLOptions options)
        => !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? options.BaseUrl!.TrimEnd('/')
            : options.ApiKey is { } key && key.EndsWith(":fx", StringComparison.Ordinal)
                ? "https://api-free.deepl.com"
                : "https://api.deepl.com";

    private static string? Target(string language) => language switch
    {
        "ar" => "AR",
        "de" => "DE",
        "en" => "EN-US",
        _ => null
    };

    private static string? Source(string? language) => language switch
    {
        "ar" => "AR",
        "de" => "DE",
        "en" => "EN",
        _ => null
    };

    public async Task<TranslationBatchResult> TranslateAsync(
        IReadOnlyList<string> texts, string? sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        if (!IsAvailable) return TranslationBatchResult.Fail(TranslationFailureKind.Permanent);
        var target = Target(targetLanguage);
        if (target is null || texts.Count == 0) return TranslationBatchResult.Fail(TranslationFailureKind.Permanent);

        var body = new DeepLRequest
        {
            Text = texts.ToList(),
            TargetLang = target,
            SourceLang = Source(sourceLanguage),
            // Keep the owner's line breaks; do not let the vendor reformat punctuation or add formality.
            PreserveFormatting = true
        };

        try
        {
            var client = _factory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, ResolveBaseUrl(_options) + "/v2/translate")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _options.ApiKey);

            using var response = await client.SendAsync(request, ct);

            if ((int)response.StatusCode == 456) return TranslationBatchResult.Fail(TranslationFailureKind.QuotaExceeded);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                return TranslationBatchResult.Fail(TranslationFailureKind.Transient);
            if (!response.IsSuccessStatusCode) return TranslationBatchResult.Fail(TranslationFailureKind.Permanent);

            var payload = await response.Content.ReadFromJsonAsync<DeepLResponse>(cancellationToken: ct);
            if (payload?.Translations is null || payload.Translations.Count != texts.Count)
                return TranslationBatchResult.Fail(TranslationFailureKind.Permanent);

            return TranslationBatchResult.Success(
                payload.Translations.Select(t => new TranslatedText(t.Text ?? string.Empty, t.DetectedSourceLanguage?.ToLowerInvariant())).ToList());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return TranslationBatchResult.Fail(TranslationFailureKind.Timeout);
        }
        catch (HttpRequestException)
        {
            return TranslationBatchResult.Fail(TranslationFailureKind.Transient);
        }
        catch (System.Text.Json.JsonException)
        {
            return TranslationBatchResult.Fail(TranslationFailureKind.Permanent);
        }
    }

    private sealed class DeepLRequest
    {
        [JsonPropertyName("text")] public List<string> Text { get; set; } = [];
        [JsonPropertyName("target_lang")] public string TargetLang { get; set; } = string.Empty;
        [JsonPropertyName("source_lang")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceLang { get; set; }
        [JsonPropertyName("preserve_formatting")] public bool PreserveFormatting { get; set; }
    }

    private sealed class DeepLResponse
    {
        [JsonPropertyName("translations")] public List<DeepLItem>? Translations { get; set; }
    }

    private sealed class DeepLItem
    {
        [JsonPropertyName("detected_source_language")] public string? DetectedSourceLanguage { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}

/// <summary>Registered when no vendor is configured: reports itself unavailable so nothing is claimed, sent or spent.</summary>
public sealed class NoTranslationProvider : ITranslationProvider
{
    public string Name => "none";
    public bool IsAvailable => false;

    public Task<TranslationBatchResult> TranslateAsync(
        IReadOnlyList<string> texts, string? sourceLanguage, string targetLanguage, CancellationToken ct)
        => Task.FromResult(TranslationBatchResult.Fail(TranslationFailureKind.Permanent));
}
