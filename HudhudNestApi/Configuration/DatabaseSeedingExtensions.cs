using HudhudNestApi.Infrastructure.Persistence.Seeds;

namespace HudhudNestApi.Configuration;

public static class DatabaseSeedingExtensions
{
    /// <summary>
    /// <c>true</c> = seed reference data on a background service after Kestrel is already listening;
    /// <c>false</c> = seed inline before the host starts listening. Defaults to background in
    /// Production/Staging, inline everywhere else.
    /// </summary>
    public const string BackgroundConfigurationKey = "Database:SeedReferenceData:Background";

    /// <summary>Resolves <see cref="BackgroundConfigurationKey"/> for the given environment.</summary>
    public static bool ShouldSeedInBackground(IConfiguration configuration, IHostEnvironment environment) =>
        configuration.GetValue<bool?>(BackgroundConfigurationKey)
        ?? (environment.IsProduction() || environment.IsStaging());

    /// <summary>
    /// Registers <see cref="ReferenceDataSeedHostedService"/> when seeding should not block startup.
    /// Call before <c>builder.Build()</c>.
    /// </summary>
    public static IServiceCollection AddReferenceDataSeeding(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (ShouldSeedInBackground(configuration, environment))
        {
            services.AddHostedService<ReferenceDataSeedHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Inline seeding for the cases where background seeding is off (Development, Testing, CI).
    /// A no-op when <see cref="ReferenceDataSeedHostedService"/> owns seeding.
    /// </summary>
    public static async Task SeedReferenceDataAsync(this WebApplication app)
    {
        if (ShouldSeedInBackground(app.Configuration, app.Environment))
        {
            return;
        }

        await SeedOnceAsync(app.Services, CancellationToken.None);
    }

    // BUG FIX: DatabaseSeeder.SeedAsync existed in the codebase (governorates,
    // districts, neighborhoods, property types, currencies, roles) but was never
    // actually called from anywhere — dead code, which is the reason those lookup
    // tables were empty in practice. All the individual seed methods are
    // idempotent ("ensure exists" per row), so running this on every startup is
    // safe and cheap; it must NOT block the app from serving requests if it fails
    // (e.g. DB not migrated yet on first deploy), so failures are logged, not
    // thrown.
    internal static async Task<bool> SeedOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var seedScope = services.CreateScope();

        try
        {
            await DatabaseSeeder.SeedAsync(seedScope.ServiceProvider, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            seedScope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("DatabaseSeeder")
                .LogError(ex, "Reference-data seeding failed — lookup tables " +
                    "(governorates/districts/neighborhoods/property types) may be incomplete " +
                    "until this succeeds.");
            return false;
        }
    }
}

/// <summary>
/// Seeds reference data after the host is up, so a slow or briefly unreachable database (cold
/// Supabase/Render Postgres, a held advisory lock during a rolling deploy) can never keep Kestrel
/// from listening. Before this existed the seed ran inline in <c>Program.cs</c> with no time bound,
/// so every restart answered 503 from Render's edge until the database finished — and indefinitely
/// if it never did, which is how a single slow dependency became a multi-hour outage of
/// <c>/health/live</c> (a probe that, by design, checks nothing but the process).
/// </summary>
public sealed class ReferenceDataSeedHostedService : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(2),
    ];

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<ReferenceDataSeedHostedService> _logger;
    private readonly Func<IServiceProvider, CancellationToken, Task<bool>> _seedOnce;
    private readonly TimeSpan[] _retryDelays;
    private readonly TimeSpan _attemptTimeout;

    public ReferenceDataSeedHostedService(
        IServiceProvider services,
        ILogger<ReferenceDataSeedHostedService> logger)
        : this(services, logger, DatabaseSeedingExtensions.SeedOnceAsync, RetryDelays, AttemptTimeout)
    {
    }

    // Test seam: lets the retry, timeout and shutdown paths be exercised in milliseconds without a
    // database. Not public, so the container keeps resolving the two-argument constructor.
    internal ReferenceDataSeedHostedService(
        IServiceProvider services,
        ILogger<ReferenceDataSeedHostedService> logger,
        Func<IServiceProvider, CancellationToken, Task<bool>> seedOnce,
        TimeSpan[] retryDelays,
        TimeSpan attemptTimeout)
    {
        _services = services;
        _logger = logger;
        _seedOnce = seedOnce;
        _retryDelays = retryDelays;
        _attemptTimeout = attemptTimeout;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let Kestrel bind and the first health probes be answered before any database work starts.
        await Task.Yield();

        for (var attempt = 0; attempt <= _retryDelays.Length; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            attemptCts.CancelAfter(_attemptTimeout);

            try
            {
                if (await _seedOnce(_services, attemptCts.Token))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError(
                    "Reference-data seeding attempt {Attempt} exceeded {Timeout}.",
                    attempt + 1,
                    _attemptTimeout);
            }

            if (attempt == _retryDelays.Length)
            {
                break;
            }

            try
            {
                await Task.Delay(_retryDelays[attempt], stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        _logger.LogError(
            "Reference-data seeding gave up after {Attempts} attempts; the API keeps serving. " +
            "Restart the instance once the database is healthy.",
            _retryDelays.Length + 1);
    }
}
