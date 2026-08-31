using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PropertyApi.Configuration;
using Xunit;

namespace PropertyApi.Integration.Tests.Security;

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
                "PropertyApi",
                "Program.cs"));

        var registrationSource = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "PropertyApi",
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
            var candidate = Path.Combine(current, "PropertyApi", "Program.cs");

            if (File.Exists(candidate))
                return candidate;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not find PropertyApi/Program.cs.");
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
                        "PropertyApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the PropertyApi repository root.");
    }
}
