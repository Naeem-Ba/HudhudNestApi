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
        Assert.Contains("dotnet format", yaml);
        Assert.Contains("check-vulnerable-packages.ps1", yaml);
        Assert.Contains("VULNERABILITY_BASELINE_PATH", yaml);
        Assert.Contains("XPlat Code Coverage", yaml);
        Assert.Contains("check-coverage-baseline.ps1", yaml);
        Assert.Contains("dotnet-reportgenerator-globaltool", yaml);
        Assert.Contains("upload-artifact", yaml);
        Assert.Contains("ci-quality-gate-reports", yaml);
    }

    [Fact(DisplayName = "CI quality gate scripts and baseline must be present")]
    public void CiPipeline_Should_Include_Quality_Gate_Scripts_And_Baseline()
    {
        var repoRoot = FindRepositoryRoot();

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "check-vulnerable-packages.ps1")),
            "Vulnerability gate script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "check-coverage-baseline.ps1")),
            "Coverage baseline gate script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "coverage-baseline.json")),
            "Coverage baseline file was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "vulnerability-baseline.json")),
            "Vulnerability baseline file was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "scripts", "verify-production-gate.ps1")),
            "Static production gate script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "scripts", "smoke-staging.sh")),
            "Staging smoke test script was not found.");
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
