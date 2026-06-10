using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Services;

/// <summary>
/// Periodic cleanup for expired OTP records. OTP codes are short-lived; keeping them forever
/// wastes storage and slows long-term queries.
/// </summary>
public sealed class OtpCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OtpCleanupHostedService> _logger;

    public OtpCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<OtpCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OTP cleanup service started. Interval: {Interval}.", CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OTP cleanup failed.");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    internal async Task CleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOtpCodeRepository>();

        var deleted = await repository.DeleteExpiredAsync(DateTime.UtcNow, ct);

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} expired OTP codes.", deleted);
        }
    }
}
