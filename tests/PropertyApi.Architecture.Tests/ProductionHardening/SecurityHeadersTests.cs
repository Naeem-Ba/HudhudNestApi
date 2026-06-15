namespace PropertyApi.Architecture.Tests.ProductionHardening;

public sealed class SecurityHeadersTests
{
    [Fact(DisplayName = "API must register and use security headers middleware")]
    public void Program_Should_Register_Security_Headers_Middleware()
    {
        var repoRoot = FindRepositoryRoot();
        var programFile = Path.Combine(repoRoot, "PropertyApi", "Program.cs");
        var source = File.ReadAllText(programFile);

        Assert.Contains("AddPropertyApiSecurityHeaders", source);
        Assert.Contains("UsePropertyApiSecurityHeaders", source);
    }

    [Fact(DisplayName = "Security headers middleware must include core browser hardening headers")]
    public void SecurityHeadersMiddleware_Should_Set_Core_Headers()
    {
        var repoRoot = FindRepositoryRoot();
        var middlewareFile = Path.Combine(repoRoot, "PropertyApi", "Security", "Headers", "SecurityHeadersMiddleware.cs");
        var source = File.ReadAllText(middlewareFile);

        Assert.Contains("X-Content-Type-Options", source);
        Assert.Contains("X-Frame-Options", source);
        Assert.Contains("Content-Security-Policy", source);
        Assert.Contains("Referrer-Policy", source);
        Assert.Contains("Permissions-Policy", source);
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
