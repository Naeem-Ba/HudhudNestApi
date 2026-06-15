using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PropertyApi.Integration.Tests.TestInfrastructure;

namespace PropertyApi.Integration.Tests.Security;

[Trait("Category", "Security")]
[Trait("Feature", "ForwardedHeaders")]
public sealed class ForwardedHeadersTests
{
    [Fact(DisplayName = "Production ForwardedHeaders uses ForwardLimit = 1")]
    public void ForwardedHeaders_ShouldLimit_ForwardLimit_To1InProduction()
    {
        using var app = TestApplication.CreateProduction();

        var options = app.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value;

        Assert.Equal(1, options.ForwardLimit.GetValueOrDefault());
        Assert.True(options.RequireHeaderSymmetry);
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    [Fact(DisplayName = "Production rejects X-Forwarded-* headers from an unknown proxy")]
    public async Task ForwardedHeaders_ShouldReject_UnknownProxiesInProduction()
    {
        using var app = TestApplication.CreateProduction();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "api.example.test");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(
            body.Contains("unknown proxies", StringComparison.OrdinalIgnoreCase),
            $"Expected response body to explain the unknown proxy rejection. Body: {body}");
    }

    [Fact(DisplayName = "Program.cs requires known proxies or known networks in Production")]
    public void ForwardedHeaders_ShouldRequire_KnownProxyOrKnownNetworkInProduction()
    {
        var programSource = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "PropertyApi", "Program.cs"));

        Assert.Contains("ForwardedHeaders:KnownProxies", programSource);
        Assert.Contains("ForwardedHeaders:KnownNetworks", programSource);
        Assert.True(
            programSource.Contains("required in Production", StringComparison.OrdinalIgnoreCase),
            "Program.cs should fail startup in Production when no known proxy/network is configured.");
        Assert.False(
            programSource.Contains(
                "KnownNetworks.Clear();\n    forwardedHeadersOptions.KnownProxies.Clear();",
                StringComparison.Ordinal),
            "Program.cs must not clear trusted proxy/network lists as a Production fallback.");
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
