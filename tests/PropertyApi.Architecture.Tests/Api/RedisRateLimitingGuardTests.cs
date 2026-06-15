namespace PropertyApi.Architecture.Tests.Api;

public sealed class RedisRateLimitingGuardTests
{
    [Fact(DisplayName = "Redis rate limiting middleware must partition by IP and use atomic Redis increment")]
    public void RedisRateLimitingMiddleware_Should_Use_Ip_Partition_And_Redis_Atomic_Increment()
    {
        var repoRoot = FindRepositoryRoot();
        var middlewareFile = Path.Combine(repoRoot, "PropertyApi", "Security", "RateLimiting", "RedisRateLimitingMiddleware.cs");
        var partitionFile = Path.Combine(repoRoot, "PropertyApi", "Security", "RateLimiting", "RedisRateLimitPartitionKeyResolver.cs");

        var middlewareSource = File.ReadAllText(middlewareFile);
        var partitionSource = File.ReadAllText(partitionFile);

        Assert.Contains("redis.call('INCR'", middlewareSource);
        Assert.Contains("redis.call('PEXPIRE'", middlewareSource);
        Assert.Contains("RemoteIpAddress", partitionSource);
        Assert.Contains("ip:", partitionSource);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing PropertyApi.sln.");
    }
}
