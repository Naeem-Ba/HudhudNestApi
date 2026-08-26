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

    [Fact(DisplayName = "Auth refresh endpoint must use auth-refresh rate limiting policy")]
    public void Refresh_Should_Have_AuthRefresh_RateLimit()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Refresh));
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal("auth-refresh", attribute.PolicyName);
    }

    [Fact(DisplayName = "Auth logout endpoint must use auth-logout rate limiting policy")]
    public void Logout_Should_Have_AuthLogout_RateLimit()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Logout));
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal("auth-logout", attribute.PolicyName);
    }

    [Theory(DisplayName = "Public property searches must use their distributed rate limiting policies")]
    [InlineData(nameof(PropertiesController.GetAll), "public-search")]
    [InlineData(nameof(PropertiesController.SearchNearby), "geo-search")]
    public void PropertySearch_Should_Have_Distributed_RateLimit(
        string methodName,
        string expectedPolicy)
    {
        var method = typeof(PropertiesController).GetMethod(methodName);
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes(
                typeof(EnableRateLimitingAttribute),
                inherit: false)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal(expectedPolicy, attribute.PolicyName);
    }

    [Fact(DisplayName = "Public agency page must use the agencies-public rate limiting policy")]
    public void GetBySlug_Should_Have_AgenciesPublic_RateLimit()
    {
        var method = typeof(AgenciesController).GetMethod(nameof(AgenciesController.GetBySlug));
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes(
                typeof(EnableRateLimitingAttribute),
                inherit: false)
            .Cast<EnableRateLimitingAttribute>());

        Assert.Equal("agencies-public", attribute.PolicyName);
    }

    [Fact(DisplayName = "Program.cs must define local fallback auth policies and enable Redis rate limiting")]
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
        Assert.Contains("UseRedisRateLimiting", source);
        Assert.Contains("AddPropertyApiRedisRateLimiting", source);
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
