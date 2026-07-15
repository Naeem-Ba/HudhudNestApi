namespace PropertyApi.Architecture.Tests;

public sealed class PerformanceAssetsTests
{
    [Fact(DisplayName = "Performance phase must include repeatable seed and baseline scripts")]
    public void Performance_Phase_Should_Include_Repeatable_Scripts()
    {
        var repoRoot = FindRepositoryRoot();

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "scripts", "perf-seed-api.ps1")),
            "Performance seed script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "scripts", "perf-baseline.ps1")),
            "Performance baseline script was not found.");

        Assert.True(
            File.Exists(Path.Combine(repoRoot, "ci", "performance-scenarios.json")),
            "Performance scenario manifest was not found.");

        var scenarios = File.ReadAllText(
            Path.Combine(repoRoot, "ci", "performance-scenarios.json"));

        Assert.Contains("properties-list-default", scenarios);
        Assert.Contains("properties-list-filtered", scenarios);
        Assert.Contains("properties-geo-search", scenarios);
        Assert.Contains("auth-login", scenarios);
        Assert.Contains("auth-refresh", scenarios);
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
