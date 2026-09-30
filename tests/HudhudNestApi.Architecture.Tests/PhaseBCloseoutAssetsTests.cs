namespace HudhudNestApi.Architecture.Tests;

public sealed class PhaseBCloseoutAssetsTests
{
    [Fact(DisplayName = "Phase B closeout must include repeatable GO NO-GO gate assets")]
    public void PhaseB_Closeout_Should_Include_Repeatable_Gate_Assets()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "phase-b-closeout.ps1");
        var docsPath = Path.Combine(repoRoot, "docs", "phase-b-closeout-gate.md");

        Assert.True(File.Exists(scriptPath), "Phase B closeout script was not found.");
        Assert.True(File.Exists(docsPath), "Phase B closeout documentation was not found.");

        var script = File.ReadAllText(scriptPath);
        var docs = File.ReadAllText(docsPath);

        Assert.Contains("GO", script);
        Assert.Contains("NO-GO", script);
        Assert.Contains("LOCAL-PASS", script);
        Assert.Contains("ReleaseCandidate", script);
        Assert.Contains("LASTEXITCODE", script);
        Assert.Contains("Assert-TestPostgresConfigured", script);
        Assert.Contains("LatencyRegressionTolerancePercent", script);
        Assert.Contains("ThroughputRegressionTolerancePercent", script);
        Assert.Contains("dotnet format", script);
        Assert.Contains("HudhudNestApi.Integration.Tests", script);
        Assert.Contains("performance-compare", script);
        Assert.Contains("failed requests", script);
        Assert.Contains("Refusing to run smoke tests", script);
        Assert.Contains("phase-b-closeout", script);

        Assert.Contains("GO Requirements", docs);
        Assert.Contains("NO-GO Conditions", docs);
        Assert.Contains("LOCAL-PASS", docs);
        Assert.Contains("Do not run smoke or load tests against production", docs);

        var postgresSetupScript = File.ReadAllText(
            Path.Combine(repoRoot, "scripts", "setup-local-test-postgres.ps1"));

        Assert.Contains("TEST_POSTGRES_CONNECTION_STRING", postgresSetupScript);
        Assert.Contains("CREATE DATABASE", postgresSetupScript);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing HudhudNestApi.sln.");
    }
}
