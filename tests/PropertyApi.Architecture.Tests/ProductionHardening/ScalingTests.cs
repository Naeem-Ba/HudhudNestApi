namespace PropertyApi.Architecture.Tests.ProductionHardening;

public sealed class ScalingTests
{
    [Fact(DisplayName = "Security stamp validator must use IDistributedCache for multi-instance deployments")]
    public void SecurityStampValidator_Should_Use_DistributedCache()
    {
        var source = ReadSource(
            "PropertyApi.Infrastructure",
            "Identity",
            "Services",
            "CachedSecurityStampValidator.cs");

        Assert.Contains("IDistributedCache", source);
        Assert.DoesNotContain("IMemoryCache", source);
        Assert.Contains("GetStringAsync", source);
        Assert.Contains("SetStringAsync", source);
    }

    [Fact(DisplayName = "SignalR must support Redis backplane configuration")]
    public void Program_Should_Add_SignalR_Redis_Backplane_When_Configured()
    {
        var source = ReadSource("PropertyApi", "Program.cs");
        var project = ReadSource("PropertyApi", "PropertyApi.csproj");

        Assert.Contains("AddStackExchangeRedis", source);
        Assert.Contains("SignalR:Provider", source);
        Assert.Contains("SignalR:Redis:ConnectionString", source);
        Assert.Contains("Microsoft.AspNetCore.SignalR.StackExchangeRedis", project);
    }

    private static string ReadSource(params string[] relativePath)
    {
        var repoRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { repoRoot }.Concat(relativePath).ToArray()));
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
