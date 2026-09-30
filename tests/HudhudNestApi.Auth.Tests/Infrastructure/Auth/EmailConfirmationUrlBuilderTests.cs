using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Auth;

/// <summary>
/// The confirmation link used to be assembled inline with a hard-coded
/// http://localhost:4200 fallback, against a configuration key that existed in no
/// appsettings file. Production therefore mailed links to localhost, silently, for every
/// account ever registered. These tests pin the three ways that must now fail loudly.
/// </summary>
public sealed class EmailConfirmationUrlBuilderTests
{
    private static readonly Guid IdentityId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Build_UsesTheConfiguredOrigin()
    {
        var builder = CreateBuilder(
            "https://app.example.com",
            Environments.Production);

        var url = builder.Build(IdentityId, "plain-token");

        Assert.Equal(
            $"https://app.example.com/#/auth/verify-email?userId={IdentityId}&token=plain-token",
            url);
    }

    [Fact]
    public void Build_EscapesTheToken()
    {
        var builder = CreateBuilder(
            "https://app.example.com",
            Environments.Production);

        // Identity's tokens are base64-ish and routinely carry +, / and =, every one of
        // which changes meaning if it reaches the query string unescaped.
        var url = builder.Build(IdentityId, "CfDJ8A+b/c=");

        Assert.EndsWith("&token=CfDJ8A%2Bb%2Fc%3D", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotCarryTheEmailAddress()
    {
        var builder = CreateBuilder(
            "https://app.example.com",
            Environments.Production);

        var url = builder.Build(IdentityId, "token");

        // Neither the verify endpoint nor the frontend page reads it, so it was personal
        // data sitting in a URL for no reason.
        Assert.DoesNotContain("email=", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TrimsATrailingSlash()
    {
        var builder = CreateBuilder(
            "https://app.example.com/",
            Environments.Production);

        Assert.Contains(
            "https://app.example.com/#/auth/verify-email",
            builder.Build(IdentityId, "token"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FallsBackToLocalhost_OutsideProduction()
    {
        var builder = CreateBuilder(
            configuredBaseUrl: null,
            Environments.Development);

        Assert.StartsWith(
            "http://localhost:4200/#/auth/verify-email",
            builder.Build(IdentityId, "token"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Throws_WhenProductionHasNoConfiguredOrigin()
    {
        var builder = CreateBuilder(
            configuredBaseUrl: null,
            Environments.Production);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(IdentityId, "token"));

        Assert.Contains("Frontend:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Throws_WhenProductionIsConfiguredWithPlainHttp()
    {
        var builder = CreateBuilder(
            "http://app.example.com",
            Environments.Production);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(IdentityId, "token"));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  https://app.example.com/  ")]
    [InlineData("https://app.example.com//")]
    public void Build_NormalisesSurroundingWhitespaceAndTrailingSlashes_WithoutDoubleSlash(
        string configured)
    {
        var url = CreateBuilder(configured, Environments.Production)
            .Build(IdentityId, "token");

        Assert.StartsWith("https://app.example.com/#/auth/verify-email?", url, StringComparison.Ordinal);
        Assert.DoesNotContain("com//", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AcceptsPlainHttp_InDevelopment()
    {
        var url = CreateBuilder("http://localhost:4200", Environments.Development)
            .Build(IdentityId, "token");

        Assert.StartsWith("http://localhost:4200/#/", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Throws_WhenProductionOriginContainsWhitespace()
    {
        var builder = CreateBuilder("https://app example.com", Environments.Production);

        Assert.Throws<InvalidOperationException>(() => builder.Build(IdentityId, "token"));
    }

    [Fact]
    public void Build_Throws_WhenTheConfiguredOriginIsRelative()
    {
        var builder = CreateBuilder(
            "/auth",
            Environments.Development);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(IdentityId, "token"));

        Assert.Contains("absolute", exception.Message, StringComparison.Ordinal);
    }

    private static EmailConfirmationUrlBuilder CreateBuilder(
        string? configuredBaseUrl,
        string environmentName)
    {
        var settings = new Dictionary<string, string?>();

        if (configuredBaseUrl is not null)
        {
            settings["Frontend:BaseUrl"] = configuredBaseUrl;
        }

        return new EmailConfirmationUrlBuilder(
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
