using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace PropertyApi.Integration.Tests.Security;

public sealed class ForwardedHeadersTests
{
    [Fact(DisplayName = "Production ForwardedHeaders supports Render/Cloudflare asymmetric proxy headers")]
    public void ForwardedHeaders_ShouldAllow_NonSymmetricHeaders_ForRenderProxy()
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
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            options.ForwardLimit = null;
        });

        using var app = builder.Build();

        var options = app.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value;

        Assert.False(options.RequireHeaderSymmetry);
        Assert.Null(options.ForwardLimit);
        Assert.Empty(options.KnownNetworks);
        Assert.Empty(options.KnownProxies);

        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact(DisplayName = "Program.cs documents Render/Cloudflare forwarded header compatibility")]
    public void ProgramCs_ShouldDocument_RenderCloudflareForwardedHeadersCompatibility()
    {
        var programPath = FindProgramCsPath();
        var programSource = File.ReadAllText(programPath);

        Assert.Contains("RequireHeaderSymmetry = false", programSource);
        Assert.Contains("ForwardLimit = null", programSource);
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