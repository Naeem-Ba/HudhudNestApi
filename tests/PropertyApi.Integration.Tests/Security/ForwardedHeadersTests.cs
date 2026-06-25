using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace PropertyApi.Integration.Tests.Security;

public sealed class ForwardedHeadersTests
{
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

    [Fact(DisplayName = "Program.cs keeps forwarded headers configurable and bounded")]
    public void ProgramCs_ShouldKeepForwardedHeaders_ConfigurableAndBounded()
    {
        var programPath = FindProgramCsPath();
        var programSource = File.ReadAllText(programPath);

        Assert.Contains("RequireHeaderSymmetry = false", programSource);
        Assert.Contains("ForwardedHeaders", programSource);
        Assert.Contains("ForwardLimit", programSource);
        Assert.Contains("KnownProxies", programSource);
        Assert.Contains("KnownNetworks", programSource);
        Assert.Contains("TrustAllProxies", programSource);
        Assert.Contains("XForwardedProto", programSource);
        Assert.Contains("XForwardedHost", programSource);
        Assert.Contains("UseForwardedHeaders", programSource);
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
}
