using Xunit;

namespace PropertyApi.Integration.Tests.Health;

public sealed class HealthCheckTests
{
    [Fact(DisplayName =
        "Program must expose liveness/readiness endpoints and register PostgreSQL and Redis checks")]
    public void Program_Should_Map_Operational_Health_Endpoints()
    {
        var repoRoot = FindRepositoryRoot();

        var program = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "PropertyApi",
                "Program.cs"));

        var dependencyInjection = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "PropertyApi.Infrastructure",
                "DependencyInjection.cs"));

        var healthEndpoints = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "PropertyApi",
                "Health",
                "HealthEndpointExtensions.cs"));

        var postGisHealthCheck = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "PropertyApi.Infrastructure",
                "Health",
                "PostGisHealthCheck.cs"));

        Assert.Contains(
            "MapOperationalHealthEndpoints",
            program);

        Assert.Contains(
            "/health/live",
            healthEndpoints);

        Assert.Contains(
            "/health/ready",
            healthEndpoints);

        Assert.Contains(
            "AddCheck<PostGisHealthCheck>",
            dependencyInjection);

        Assert.Contains(
            "\"postgresql-postgis\"",
            dependencyInjection);

        Assert.Contains(
            "AddCheck<DistributedCacheHealthCheck>",
            dependencyInjection);

        Assert.Contains(
            "\"redis\"",
            dependencyInjection);

        Assert.Contains(
            "\"ready\"",
            dependencyInjection);

        Assert.Contains(
            "pg_extension",
            postGisHealthCheck);

        Assert.Contains(
            "IX_Properties_GeoLocation",
            postGisHealthCheck);

        Assert.Contains(
            "GeoLocation",
            postGisHealthCheck);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "PropertyApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }

}
