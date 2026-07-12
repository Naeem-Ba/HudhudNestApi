using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PropertyApi.Infrastructure.Health;

public sealed class DistributedCacheHealthCheck(
    IDistributedCache cache) : IHealthCheck
{
    private static readonly TimeSpan Timeout =
        TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var key =
            $"propertyapi:health:{Guid.NewGuid():N}";

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(Timeout);

        try
        {
            await cache.SetStringAsync(
                key,
                "ok",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow =
                        TimeSpan.FromSeconds(10)
                },
                timeoutSource.Token);

            var value = await cache.GetStringAsync(
                key,
                timeoutSource.Token);

            await cache.RemoveAsync(
                key,
                timeoutSource.Token);

            return string.Equals(
                value,
                "ok",
                StringComparison.Ordinal)
                ? HealthCheckResult.Healthy(
                    "Distributed cache is reachable.")
                : HealthCheckResult.Unhealthy(
                    "Distributed cache returned an unexpected value.");
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "Distributed cache health check timed out.",
                exception);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Distributed cache health check failed.",
                exception);
        }
    }
}