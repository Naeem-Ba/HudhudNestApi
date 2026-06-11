using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Controllers;

namespace PropertyApi.Architecture.Tests.Api;

public sealed class RateLimitingGuardTests
{
    [Fact(DisplayName = "Auth login endpoint must use auth-login rate limiting policy")]
    public void Login_Should_Have_AuthLogin_RateLimit()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Login));
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal("auth-login", attribute.PolicyName);
    }

    [Fact(DisplayName = "Auth register endpoint must use auth-register rate limiting policy")]
    public void Register_Should_Have_AuthRegister_RateLimit()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Register));
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal("auth-register", attribute.PolicyName);
    }

    [Fact(DisplayName = "Program.cs must define auth-login and auth-register policies")]
    public void Program_Should_Define_Auth_RateLimit_Policies()
    {
        var repoRoot = FindRepositoryRoot();
        var programFile = Path.Combine(repoRoot, "PropertyApi", "Program.cs");
        var source = File.ReadAllText(programFile);

        Assert.Contains("auth-login", source);
        Assert.Contains("PermitLimit = 10", source);
        Assert.Contains("TimeSpan.FromMinutes(1)", source);
        Assert.Contains("auth-register", source);
        Assert.Contains("PermitLimit = 5", source);
        Assert.Contains("TimeSpan.FromMinutes(10)", source);
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
