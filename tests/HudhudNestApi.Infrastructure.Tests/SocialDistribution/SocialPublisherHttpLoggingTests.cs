using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Infrastructure.SocialDistribution;
using HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

namespace HudhudNestApi.Infrastructure.Tests.SocialDistribution;

/// <summary>
/// A real platform credential must never reach the application log. Telegram's Bot API forces the
/// bot token into the request URL path (<c>/bot&lt;TOKEN&gt;/sendMessage</c>), and the default
/// <c>IHttpClientFactory</c> pipeline logs every request URI at Information level — redacting only
/// the query string, never the path — so the token would land in Render's log stream on every
/// post. These tests drive each real publisher through the production DI registration (real
/// <c>IHttpClientFactory</c>, real logging handlers; only the network is stubbed) and assert the
/// secret appears in no log message, structured value, or scope.
/// </summary>
public sealed class SocialPublisherHttpLoggingTests
{
    private const string Secret = "987654:SECRET-Token_VALUE-abc123XYZ";

    [Fact]
    public async Task TelegramPublisher_NeverWritesTheBotTokenToAnyLog()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(
            logs,
            new Dictionary<string, string?> { [$"{TelegramBotOptions.SectionName}:BotToken"] = Secret },
            TelegramBotPublisher.HttpClientName);

        var publisher = ResolvePublisher(services, SocialPlatform.Telegram);
        var result = await publisher.PublishAsync(MakeRequest(SocialPlatform.Telegram, "-1001234567890"));

        Assert.True(result.IsSuccess);
        AssertNoSecretLogged(logs);
    }

    [Fact]
    public async Task Harness_SeesTheDefaultHttpClientLogging_SoACleanResultIsNotVacuous()
    {
        // A client WITHOUT RemoveAllLoggers: if the capture harness were blind to the factory's
        // handlers, the leak tests above could pass for the wrong reason.
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(
            logs,
            new Dictionary<string, string?>(),
            "harness-probe");

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("harness-probe");
        using var response = await client.PostAsync($"https://api.example.test/bot{Secret}/ping", content: null);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains(logs.Entries, entry => entry.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TelegramPublisher_WithAPerAccountCredential_NeverWritesItToAnyLog()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(
            logs,
            new Dictionary<string, string?> { [$"{TelegramBotOptions.SectionName}:BotToken"] = "111111:shared-fallback-token" },
            TelegramBotPublisher.HttpClientName);

        var result = await ResolvePublisher(services, SocialPlatform.Telegram)
            .PublishAsync(MakeRequest(SocialPlatform.Telegram, "@hudhudnest", credentialReference: Secret));

        Assert.True(result.IsSuccess);
        AssertNoSecretLogged(logs);
    }

    [Fact]
    public async Task FacebookPublisher_NeverWritesThePageAccessTokenToAnyLog()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(
            logs,
            new Dictionary<string, string?> { [$"{FacebookGraphApiOptions.SectionName}:PageAccessToken"] = Secret },
            FacebookGraphApiPublisher.HttpClientName);

        var result = await ResolvePublisher(services, SocialPlatform.Facebook).PublishAsync(MakeRequest(SocialPlatform.Facebook, "123456789"));

        Assert.True(result.IsSuccess);
        AssertNoSecretLogged(logs);
    }

    [Fact]
    public async Task InstagramPublisher_NeverWritesTheAccessTokenToAnyLog()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(
            logs,
            new Dictionary<string, string?> { [$"{InstagramGraphApiOptions.SectionName}:AccessToken"] = Secret },
            InstagramGraphApiPublisher.HttpClientName);

        var result = await ResolvePublisher(services, SocialPlatform.Instagram).PublishAsync(MakeRequest(SocialPlatform.Instagram, "17841400000000000"));

        Assert.True(result.IsSuccess);
        AssertNoSecretLogged(logs);
    }

    private static void AssertNoSecretLogged(CapturingLoggerProvider logs)
    {
        var leaked = logs.Entries.Where(entry => entry.Contains(Secret, StringComparison.Ordinal)).ToList();
        Assert.True(leaked.Count == 0, "A platform credential reached the application log:\n" + string.Join("\n", leaked));
    }

    private static ServiceProvider BuildServices(CapturingLoggerProvider logs, Dictionary<string, string?> settings, string httpClientName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSocialDistributionInfrastructure(configuration);
        // Network stub only — every other handler in the real pipeline (logging included) stays.
        services.AddHttpClient(httpClientName).ConfigurePrimaryHttpMessageHandler(() => new StubHandler());

        return services.BuildServiceProvider();
    }

    private static ISocialPublisher ResolvePublisher(IServiceProvider services, SocialPlatform platform) =>
        services.GetServices<ISocialPublisher>().Last(publisher => publisher.Platform == platform);

    private static SocialPublishRequest MakeRequest(SocialPlatform platform, string externalAccountId, string? credentialReference = null) => new()
    {
        PublicationId = Guid.NewGuid(),
        Platform = platform,
        ExternalAccountId = externalAccountId,
        CredentialReference = credentialReference,
        Title = "عنوان",
        Body = "شقة رائعة للبيع في دمشق",
        ImageUrl = "https://res.cloudinary.test/image/upload/p.jpg",
        TargetUrl = "https://hudhudnest.com/properties/p1",
        Hashtags = Array.Empty<string>(),
        Language = "ar",
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"ok":true,"result":{"message_id":42},"id":"123456789_987654321","post_id":"123456789_987654321"}""",
                    Encoding.UTF8,
                    "application/json"),
            });
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private void Add(string text)
        {
            lock (_entries) _entries.Add(text);
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Add($"[{category}] SCOPE {state}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var structured = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(" ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                    : string.Empty;

                owner.Add($"[{category}] {logLevel}: {formatter(state, exception)} | {structured} | {exception}");
            }
        }
    }
}
