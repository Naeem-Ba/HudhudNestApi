using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HudhudNestApi.Configuration;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Security;

public sealed class ForwardedHeadersTests
{
    // BUG-01 regression: RequireHeaderSymmetry=true made ForwardedHeadersMiddleware discard
    // forwarded scheme info whenever X-Forwarded-For and X-Forwarded-Proto had mismatched
    // header counts -- which happens on Render's real Cloudflare-fronted, multi-hop edge --
    // leaving Request.IsHttps false and silently turning UseHsts() into a no-op in a genuine
    // Production environment. Unlike the test below, this one exercises the actual production
    // registration path (AddTrustedForwardedHeaders), not a hand-built stand-in options object,
    // so a regression back to RequireHeaderSymmetry=true here fails the build.
    [Fact(DisplayName =
     "AddTrustedForwardedHeaders should disable RequireHeaderSymmetry in Production")]
    public void AddTrustedForwardedHeaders_ShouldDisable_RequireHeaderSymmetry_InProduction()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.1",
            ["ForwardedHeaders:ForwardLimit"] = "1"
        });

        builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);

        using var app = builder.Build();

        var options = app.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value;

        Assert.False(options.RequireHeaderSymmetry);
    }

    // .NET 10 replaced Microsoft.AspNetCore.HttpOverrides.IPNetwork with System.Net.IPNetwork, which throws
    // when the address has bits set beyond the prefix. The old type accepted such a value (10.1.2.3/8 trusted
    // all of 10.0.0.0/8), so a value already living in the Production environment variables must keep booting.
    [Theory(DisplayName = "KnownNetworks keeps accepting CIDR values with host bits set and trusts the whole range")]
    [InlineData("10.1.2.3/8", "10.200.1.1", "11.0.0.1")]
    [InlineData("10.0.0.0/8", "10.200.1.1", "11.0.0.1")]
    [InlineData("203.0.113.77/32", "203.0.113.77", "203.0.113.78")]
    [InlineData("2001:db8:1:2::9/32", "2001:db8:ffff::1", "2001:db9::1")]
    public void AddTrustedForwardedHeaders_ShouldTrustTheWholeNetwork_EvenWhenHostBitsAreSet(
        string cidr, string insideAddress, string outsideAddress)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = cidr,
            ["ForwardedHeaders:ForwardLimit"] = "1"
        });

        builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);

        using var app = builder.Build();

        var options = app.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value;

        var network = Assert.Single(options.KnownIPNetworks);
        Assert.True(network.Contains(IPAddress.Parse(insideAddress)));
        Assert.False(network.Contains(IPAddress.Parse(outsideAddress)));
    }

    [Theory(DisplayName = "KnownNetworks rejects malformed CIDR values with a clear message")]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("not-an-ip/8")]
    [InlineData("2001:db8::/129")]
    public void AddTrustedForwardedHeaders_ShouldRejectMalformedNetworks(string cidr)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = cidr,
            ["ForwardedHeaders:ForwardLimit"] = "1"
        });

        builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);

        using var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
        Assert.Contains("ForwardedHeaders:KnownNetworks", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Production ForwardedHeaders should use bounded trusted proxy settings")]
    public void ForwardedHeaders_ShouldUse_BoundedTrustedProxySettings()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto |
                ForwardedHeaders.XForwardedHost;

            options.RequireHeaderSymmetry = false;
            options.ForwardLimit = 1;
            options.KnownProxies.Add(IPAddress.Parse("203.0.113.1"));
        });

        using var app = builder.Build();

        var options = app.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value;

        Assert.False(options.RequireHeaderSymmetry);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Contains(IPAddress.Parse("203.0.113.1"), options.KnownProxies);

        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact(DisplayName =
     "Program.cs keeps forwarded headers explicitly trusted and bounded")]
    public void ProgramCs_ShouldKeepForwardedHeaders_ConfigurableAndBounded()
    {
        var repoRoot = FindRepositoryRoot();

        var programSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "HudhudNestApi",
                "Program.cs"));

        var registrationSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "HudhudNestApi",
                "Configuration",
                "ForwardedHeadersRegistration.cs"));

        Assert.Contains(
            "AddTrustedForwardedHeaders",
            programSource);

        Assert.Contains(
            "UseForwardedHeaders",
            programSource);

        Assert.DoesNotContain(
            "TrustAllProxies",
            programSource);

        Assert.Contains(
            "KnownProxies",
            registrationSource);

        Assert.Contains(
            "KnownNetworks",
            registrationSource);

        Assert.Contains(
            "ForwardLimit",
            registrationSource);

        Assert.Contains(
            "XForwardedFor",
            registrationSource);

        Assert.Contains(
            "XForwardedProto",
            registrationSource);

        Assert.DoesNotContain(
            "TrustAllProxies",
            registrationSource);
    }

    private static string FindProgramCsPath()
    {
        var current = Directory.GetCurrentDirectory();

        while (current is not null)
        {
            var candidate = Path.Combine(current, "HudhudNestApi", "Program.cs");

            if (File.Exists(candidate))
                return candidate;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not find HudhudNestApi/Program.cs.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "HudhudNestApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the HudhudNestApi repository root.");
    }
}
