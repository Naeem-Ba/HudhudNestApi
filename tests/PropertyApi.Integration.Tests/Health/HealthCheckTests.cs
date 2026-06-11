namespace PropertyApi.Integration.Tests.Health;

public sealed class HealthCheckTests
{
    [Fact(DisplayName = "Program must expose /health endpoint and register PostGIS health check")]
    public void Program_Should_Map_Health_Endpoint_And_Register_PostGis_Check()
    {
        var repoRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repoRoot, "PropertyApi", "Program.cs"));
        var dependencyInjection = File.ReadAllText(Path.Combine(repoRoot, "PropertyApi.Infrastructure", "DependencyInjection.cs"));
        var healthCheck = File.ReadAllText(Path.Combine(repoRoot, "PropertyApi.Infrastructure", "Health", "PostGisHealthCheck.cs"));

        Assert.Contains("MapHealthChecks(\"/health\")", program);
        Assert.Contains("AddCheck<PostGisHealthCheck>(\"postgis\")", dependencyInjection);
        Assert.Contains("pg_extension", healthCheck);
        Assert.Contains("IX_Properties_GeoLocation", healthCheck);
        Assert.Contains("GeoLocation", healthCheck);
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
