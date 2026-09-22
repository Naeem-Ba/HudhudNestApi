using Xunit;

namespace HudhudNestApi.Integration.Tests.Health;

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
                "HudhudNestApi",
                "Program.cs"));

        var dependencyInjection = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "HudhudNestApi.Infrastructure",
                "DependencyInjection.cs"));

        var healthRegistration = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "HudhudNestApi.Infrastructure",
                "Health",
                "HealthInfrastructureRegistration.cs"));

        var healthEndpoints = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "HudhudNestApi",
                "Health",
                "HealthEndpointExtensions.cs"));

        var postGisHealthCheck = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "HudhudNestApi.Infrastructure",
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
            "AddOperationalHealthChecks",
            dependencyInjection);

        Assert.Contains(
            "AddCheck<PostGisHealthCheck>",
            healthRegistration);

        Assert.Contains(
            "\"postgresql-postgis\"",
            healthRegistration);

        Assert.Contains(
            "AddCheck<DistributedCacheHealthCheck>",
            healthRegistration);

        Assert.Contains(
            "\"redis\"",
            healthRegistration);

        Assert.Contains(
            "\"ready\"",
            healthRegistration);

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
                        "HudhudNestApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }

}
