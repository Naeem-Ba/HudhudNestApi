using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Integration.Tests.Startup;

/// <summary>
/// The background seeder is what keeps a slow database from holding Kestrel back, so its retry,
/// per-attempt timeout and shutdown behaviour are pinned here with a fake seed delegate (no
/// database, delays of a few milliseconds). Lives in the integration suite because the changed-code
/// coverage gate counts lines only from instrumented suites.
/// </summary>
public sealed class ReferenceDataSeedHostedServiceTests
{
    private static readonly TimeSpan[] NoDelays = [TimeSpan.Zero, TimeSpan.Zero];

    private sealed class CapturingLogger : ILogger<ReferenceDataSeedHostedService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static ReferenceDataSeedHostedService Create(
        CapturingLogger logger,
        Func<IServiceProvider, CancellationToken, Task<bool>> seedOnce,
        TimeSpan[]? delays = null,
        TimeSpan? attemptTimeout = null) =>
        new(
            new ServiceCollection().BuildServiceProvider(),
            logger,
            seedOnce,
            delays ?? NoDelays,
            attemptTimeout ?? TimeSpan.FromSeconds(30));

    private static async Task RunToCompletionAsync(ReferenceDataSeedHostedService service, CancellationToken stopping = default)
    {
        await service.StartAsync(stopping);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "A successful first attempt seeds once and stops")]
    public async Task FirstAttemptSucceeds_SeedsOnce()
    {
        var calls = 0;
        var logger = new CapturingLogger();
        var service = Create(logger, (_, _) => { calls++; return Task.FromResult(true); });

        await RunToCompletionAsync(service);

        Assert.Equal(1, calls);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact(DisplayName = "A failed attempt is retried until it succeeds")]
    public async Task FailureThenSuccess_RetriesUntilSeeded()
    {
        var calls = 0;
        var logger = new CapturingLogger();
        var service = Create(logger, (_, _) => Task.FromResult(++calls == 3));

        await RunToCompletionAsync(service);

        Assert.Equal(3, calls);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("gave up"));
    }

    [Fact(DisplayName = "After the last retry it gives up, logs once and leaves the host running")]
    public async Task EveryAttemptFails_GivesUpAfterAllRetries()
    {
        var calls = 0;
        var logger = new CapturingLogger();
        var service = Create(logger, (_, _) => { calls++; return Task.FromResult(false); });

        await RunToCompletionAsync(service);

        Assert.Equal(NoDelays.Length + 1, calls);
        var gaveUp = Assert.Single(logger.Entries, e => e.Message.Contains("gave up"));
        Assert.Equal(LogLevel.Error, gaveUp.Level);
        Assert.Contains((NoDelays.Length + 1).ToString(), gaveUp.Message);
    }

    [Fact(DisplayName = "An attempt that exceeds the timeout is cancelled, logged and retried")]
    public async Task AttemptExceedsTimeout_IsCancelledAndRetried()
    {
        var calls = 0;
        var logger = new CapturingLogger();
        var service = Create(
            logger,
            async (_, token) =>
            {
                if (++calls == 1)
                {
                    await Task.Delay(Timeout.Infinite, token);
                }

                return true;
            },
            attemptTimeout: TimeSpan.FromMilliseconds(50));

        await RunToCompletionAsync(service);

        Assert.Equal(2, calls);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("exceeded"));
    }

    [Fact(DisplayName = "Host shutdown during an attempt stops seeding without retrying or logging a failure")]
    public async Task ShutdownDuringAttempt_StopsWithoutRetry()
    {
        var calls = 0;
        var entered = new TaskCompletionSource();
        var logger = new CapturingLogger();
        var service = Create(
            logger,
            async (_, token) =>
            {
                calls++;
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return true;
            });

        await service.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact(DisplayName = "Host shutdown while waiting between retries stops seeding")]
    public async Task ShutdownDuringRetryDelay_StopsWithoutRetry()
    {
        var calls = 0;
        var firstAttemptDone = new TaskCompletionSource();
        var logger = new CapturingLogger();
        var service = Create(
            logger,
            (_, _) =>
            {
                calls++;
                firstAttemptDone.TrySetResult();
                return Task.FromResult(false);
            },
            delays: [TimeSpan.FromHours(1)]);

        await service.StartAsync(CancellationToken.None);
        await firstAttemptDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("gave up"));
    }

    [Fact(DisplayName = "SeedOnceAsync logs and returns false when seeding throws, so the service can retry")]
    public async Task SeedOnce_ServiceFailure_ReturnsFalse()
    {
        // No AppDbContext registered: DatabaseSeeder.SeedAsync throws, exactly like an unreachable
        // database would. ILoggerFactory is what the failure path asks the scope for.
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        Assert.False(await DatabaseSeedingExtensions.SeedOnceAsync(services, CancellationToken.None));
    }

    [Fact(DisplayName = "SeedOnceAsync lets host shutdown cancellation through instead of swallowing it")]
    public async Task SeedOnce_CancelledByShutdown_Rethrows()
    {
        using var cts = new CancellationTokenSource();
        var cancelled = new Mock<IServiceProvider>();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(new ThrowOnResolveProvider(cts));
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        cancelled.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DatabaseSeedingExtensions.SeedOnceAsync(cancelled.Object, cts.Token));
    }

    private sealed class ThrowOnResolveProvider(CancellationTokenSource cts) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }
    }

    [Theory(DisplayName = "AddReferenceDataSeeding registers the background service only where it is on")]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    public void AddReferenceDataSeeding_RegistersHostedServiceByEnvironment(string environmentName, bool expected)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        var services = new ServiceCollection();

        services.AddReferenceDataSeeding(new ConfigurationBuilder().Build(), env.Object);

        Assert.Equal(
            expected,
            services.Any(d => d.ImplementationType == typeof(ReferenceDataSeedHostedService)));
    }

    private sealed class SeederCategoryProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName == "DatabaseSeeder" ? new Sink(Messages) : Mock.Of<ILogger>();

        public void Dispose()
        {
        }

        private sealed class Sink(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }

    [Theory(DisplayName = "SeedReferenceDataAsync seeds inline only where the background service is off")]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public async Task SeedReferenceDataAsync_RunsInlineOnlyOutsideBackgroundMode(string environmentName, bool attempted)
    {
        // No AppDbContext is registered, so an inline attempt fails and logs under "DatabaseSeeder";
        // that log line is the observable difference between the two modes.
        var provider = new SeederCategoryProvider();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(provider);
        await using var app = builder.Build();

        await app.SeedReferenceDataAsync();

        Assert.Equal(attempted, provider.Messages.Count > 0);
    }
}
