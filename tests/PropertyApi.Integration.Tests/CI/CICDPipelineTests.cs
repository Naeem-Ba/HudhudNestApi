namespace PropertyApi.Integration.Tests.CI;

public sealed class CICDPipelineTests
{
    [Fact(DisplayName = "CI pipeline must run PostgreSQL with PostGIS and apply migrations")]
    public void CiPipeline_Should_Use_PostGis_And_Run_Migrations()
    {
        var repoRoot = FindRepositoryRoot();
        var ciFile = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");

        Assert.True(File.Exists(ciFile), "CI workflow file was not found.");

        var yaml = File.ReadAllText(ciFile);

        Assert.Contains("postgis/postgis", yaml);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS postgis", yaml);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", yaml);
        Assert.Contains("tools/PropertyApi.Migrator/PropertyApi.Migrator.csproj", yaml);
        Assert.Contains("dotnet test", yaml);
        Assert.Contains("ConnectionStrings__DefaultConnection", yaml);
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
