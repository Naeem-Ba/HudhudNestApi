using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Architecture.Tests.Api;

public sealed class CorsRegistrationTests
{
    [Fact(DisplayName = "Cors:AllowedOriginPatterns matches a Netlify deploy-preview origin")]
    public void AllowedOriginPatterns_Matches_DeployPreviewOrigin()
    {
        var policy = BuildDefaultPolicy(
            allowedOrigins: new[] { "https://hudhudnest.com" },
            allowedOriginPatterns: new[] { @"^https://deploy-preview-\d+--hudhudnest\.netlify\.app$" });

        Assert.True(policy.IsOriginAllowed("https://deploy-preview-101--hudhudnest.netlify.app"));
    }

    [Fact(DisplayName = "Cors:AllowedOriginPatterns still rejects an unrelated origin")]
    public void AllowedOriginPatterns_Rejects_UnrelatedOrigin()
    {
        var policy = BuildDefaultPolicy(
            allowedOrigins: new[] { "https://hudhudnest.com" },
            allowedOriginPatterns: new[] { @"^https://deploy-preview-\d+--hudhudnest\.netlify\.app$" });

        Assert.False(policy.IsOriginAllowed("https://evil.example.com"));
    }

    [Fact(DisplayName = "Cors:AllowedOriginPatterns still allows the exact configured origin")]
    public void AllowedOriginPatterns_StillAllows_ExactConfiguredOrigin()
    {
        var policy = BuildDefaultPolicy(
            allowedOrigins: new[] { "https://hudhudnest.com" },
            allowedOriginPatterns: new[] { @"^https://deploy-preview-\d+--hudhudnest\.netlify\.app$" });

        Assert.True(policy.IsOriginAllowed("https://hudhudnest.com"));
    }

    [Fact(DisplayName = "Without Cors:AllowedOriginPatterns, only the exact configured origin is allowed")]
    public void NoPatternsConfigured_OnlyExactOriginAllowed()
    {
        var policy = BuildDefaultPolicy(
            allowedOrigins: new[] { "https://hudhudnest.com" },
            allowedOriginPatterns: Array.Empty<string>());

        Assert.False(policy.IsOriginAllowed("https://deploy-preview-101--hudhudnest.netlify.app"));
    }

    [Fact(DisplayName = "With neither AllowedOrigins nor AllowedOriginPatterns configured, falls back to AllowAnyOrigin in Testing/CI")]
    public void NoOriginsAndNoPatternsConfigured_FallsBackToAllowAnyOrigin()
    {
        var policy = BuildDefaultPolicy(
            allowedOrigins: Array.Empty<string>(),
            allowedOriginPatterns: Array.Empty<string>(),
            isTestingOrCi: true);

        Assert.True(policy.AllowAnyOrigin);
    }

    private static CorsPolicy BuildDefaultPolicy(
        string[] allowedOrigins,
        string[] allowedOriginPatterns,
        bool isTestingOrCi = false)
    {
        var settings = new Dictionary<string, string?>();
        for (var i = 0; i < allowedOrigins.Length; i++)
        {
            settings[$"Cors:AllowedOrigins:{i}"] = allowedOrigins[i];
        }

        for (var i = 0; i < allowedOriginPatterns.Length; i++)
        {
            settings[$"Cors:AllowedOriginPatterns:{i}"] = allowedOriginPatterns[i];
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddHudhudNestApiCors(configuration, new TestHostEnvironment(), isTestingOrCi);

        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("DefaultCors");

        Assert.NotNull(policy);
        return policy!;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            "Staging";

        public string ApplicationName { get; set; } =
            "HudhudNestApi.Architecture.Tests";

        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
