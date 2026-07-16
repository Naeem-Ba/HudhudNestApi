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

    [Fact(DisplayName = "CI workflows must use Node 24 compatible GitHub Actions")]
    public void CiWorkflows_Should_Use_Node24_Compatible_Actions()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowFiles = new[]
        {
            Path.Combine(repoRoot, ".github", "workflows", "ci.yml"),
            Path.Combine(repoRoot, ".github", "workflows", "production-gate.yml")
        };

        foreach (var workflowFile in workflowFiles)
        {
            Assert.True(File.Exists(workflowFile), $"Workflow file was not found: {workflowFile}");

            var yaml = File.ReadAllText(workflowFile);

            Assert.Contains("actions/setup-dotnet@v5", yaml);
            Assert.Contains("actions/upload-artifact@v6", yaml);
            Assert.DoesNotContain("actions/setup-dotnet@v4", yaml);
            Assert.DoesNotContain("actions/upload-artifact@v4", yaml);
        }
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

    [Fact(DisplayName = "Staging smoke script must report failed endpoint details")]
    public void StagingSmokeScript_Should_Report_Failed_Endpoint_Details()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptFile = Path.Combine(repoRoot, "scripts", "smoke-staging.sh");

        Assert.True(File.Exists(scriptFile), "Staging smoke test script was not found.");

        var script = File.ReadAllText(scriptFile);

        Assert.Contains("request_smoke_endpoint", script);
        Assert.Contains("--location", script);
        Assert.Contains("Response preview", script);
        Assert.Contains("::error::Staging smoke check failed", script);
        Assert.Contains("SMOKE_RETRY_ATTEMPTS", script);
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
