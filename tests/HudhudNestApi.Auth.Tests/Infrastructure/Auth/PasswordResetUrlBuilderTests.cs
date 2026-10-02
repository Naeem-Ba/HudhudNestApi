using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Auth;

/// <summary>
/// Unlike <see cref="EmailConfirmationUrlBuilder" />, this builder does not assemble a path
/// itself -- the whole frontend URL comes from Frontend:PasswordResetUrl. These tests pin the
/// query-string assembly and the same fail-loud rules the confirmation builder enforces for its
/// own configured origin.
/// </summary>
public sealed class PasswordResetUrlBuilderTests
{
    [Fact]
    public void Build_AppendsEmailAndTokenToTheConfiguredUrl()
    {
        var builder = CreateBuilder(
            "https://app.example.com/auth/reset-password",
            Environments.Production);

        var url = builder.Build("user@example.com", "plain-token", requestScheme: null, requestHost: null);

        Assert.Equal(
            "https://app.example.com/auth/reset-password?email=user%40example.com&token=plain-token",
            url);
    }

    [Fact]
    public void Build_UsesAmpersand_WhenTheConfiguredUrlAlreadyHasAQueryString()
    {
        var builder = CreateBuilder(
            "https://app.example.com/auth/reset-password?lang=de",
            Environments.Production);

        var url = builder.Build("user@example.com", "token", requestScheme: null, requestHost: null);

        Assert.Contains("?lang=de&email=", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EscapesTheEmailAndToken()
    {
        var builder = CreateBuilder(
            "https://app.example.com/auth/reset-password",
            Environments.Production);

        var url = builder.Build("a b@example.com", "CfDJ8A+b/c=", requestScheme: null, requestHost: null);

        Assert.Contains("email=a%20b%40example.com", url, StringComparison.Ordinal);
        Assert.EndsWith("&token=CfDJ8A%2Bb%2Fc%3D", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_IgnoresTheRequestSchemeAndHost()
    {
        var builder = CreateBuilder(
            "https://app.example.com/auth/reset-password",
            Environments.Production);

        var url = builder.Build(
            "user@example.com",
            "token",
            requestScheme: "http",
            requestHost: "attacker.example");

        Assert.StartsWith("https://app.example.com/auth/reset-password", url, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.example", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FallsBackToLocalhost_OutsideProduction()
    {
        var builder = CreateBuilder(
            configuredResetUrl: null,
            Environments.Development);

        Assert.StartsWith(
            "http://localhost:4200/auth/reset-password",
            builder.Build("user@example.com", "token", requestScheme: null, requestHost: null),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Throws_WhenProductionHasNoConfiguredUrl()
    {
        var builder = CreateBuilder(
            configuredResetUrl: null,
            Environments.Production);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build("user@example.com", "token", requestScheme: null, requestHost: null));

        Assert.Contains("Frontend:PasswordResetUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Throws_WhenProductionIsConfiguredWithPlainHttp()
    {
        var builder = CreateBuilder(
            "http://app.example.com/auth/reset-password",
            Environments.Production);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build("user@example.com", "token", requestScheme: null, requestHost: null));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Throws_WhenTheConfiguredUrlIsRelative()
    {
        var builder = CreateBuilder(
            "/auth/reset-password",
            Environments.Development);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build("user@example.com", "token", requestScheme: null, requestHost: null));

        Assert.Contains("absolute", exception.Message, StringComparison.Ordinal);
    }

    private static PasswordResetUrlBuilder CreateBuilder(
        string? configuredResetUrl,
        string environmentName)
    {
        var settings = new Dictionary<string, string?>();

        if (configuredResetUrl is not null)
        {
            settings["Frontend:PasswordResetUrl"] = configuredResetUrl;
        }

        return new PasswordResetUrlBuilder(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build(),
            new TestEnvironment(environmentName));
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HudhudNestApi.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
