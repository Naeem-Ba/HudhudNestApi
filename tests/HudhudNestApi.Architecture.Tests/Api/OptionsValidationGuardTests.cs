using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using HudhudNestApi.Infrastructure;
using HudhudNestApi.Infrastructure.Media;
using HudhudNestApi.Observability;
using HudhudNestApi.Security.Csrf;

namespace HudhudNestApi.Architecture.Tests.Api;

public sealed class OptionsValidationGuardTests
{
    [Fact(DisplayName = "Cloudinary options must fail fast when required settings are missing")]
    public void CloudinaryOptions_Should_Validate_Required_Settings()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(
            CreateInfrastructureConfiguration(includeCloudinary: false),
            new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CloudinaryOptions>>().Value);

        Assert.Contains(
            "Cloudinary:CloudName",
            exception.Message);
    }

    [Fact(DisplayName = "Cloudinary options must support the standard CLOUDINARY_URL secret")]
    public void CloudinaryOptions_Should_Bind_CloudinaryUrl()
    {
        var services = new ServiceCollection();
        var configuration = CreateInfrastructureConfiguration(includeCloudinary: false);
        configuration["CLOUDINARY_URL"] =
            "cloudinary://api-key:api%2Fsecret@sample-cloud";

        services.AddInfrastructure(configuration, new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<CloudinaryOptions>>()
            .Value;

        Assert.Equal("sample-cloud", options.CloudName);
        Assert.Equal("api-key", options.ApiKey);
        Assert.Equal("api/secret", options.ApiSecret);
    }

    [Fact(DisplayName = "Cloudinary options must reject a malformed CLOUDINARY_URL secret")]
    public void CloudinaryOptions_Should_Reject_Malformed_CloudinaryUrl()
    {
        var services = new ServiceCollection();
        var configuration = CreateInfrastructureConfiguration(includeCloudinary: false);
        configuration["CLOUDINARY_URL"] = "https://sample-cloud/credentials";

        services.AddInfrastructure(configuration, new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CloudinaryOptions>>().Value);
    }

    [Fact(DisplayName = "Cookie CSRF options must require an auth cookie name when enabled")]
    public void CookieCsrfOptions_Should_Validate_Cookie_Name_When_Enabled()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["CookieCsrf:Enabled"] = "true",
                    ["CookieCsrf:AuthenticationCookieName"] = ""
                })
            .Build();

        services.AddHudhudNestApiAntiforgery(
            configuration,
            new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CookieCsrfOptions>>().Value);

        Assert.Contains(
            "CookieCsrf:AuthenticationCookieName",
            exception.Message);
    }

    [Fact(DisplayName = "Observability options must reject invalid OTLP endpoint configuration")]
    public void ObservabilityOptions_Should_Validate_Otlp_Endpoint()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Observability:ServiceName"] = "HudhudNestApi",
                ["Observability:CorrelationHeaderName"] = "X-Correlation-ID",
                ["Observability:Otlp:Endpoint"] = "not-a-uri"
            });

        builder.AddHudhudNestApiObservability();

        using var provider = builder.Services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<HudhudNestApiObservabilityOptions>>().Value);

        Assert.Contains(
            "Observability:Otlp:Endpoint",
            exception.Message);
    }

    private static IConfiguration CreateInfrastructureConfiguration(
        bool includeCloudinary)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Database=hudhudnest;Username=test;Password=test",

            ["Jwt:Key"] =
                "0123456789abcdef0123456789abcdef",

            ["Jwt:Issuer"] =
                "HudhudNestApi.Tests",

            ["Jwt:Audience"] =
                "HudhudNestApi.Tests",

            ["Jwt:AccessTokenMinutes"] =
                "30",

            ["Jwt:RefreshTokenDays"] =
                "7",

            ["OtpSettings:SecretKey"] =
                "integration-test-otp-secret-key-at-least-32-bytes",

            ["Security:PhoneLookupHmacKey"] =
                "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="
        };

        if (includeCloudinary)
        {
            settings["Cloudinary:CloudName"] = "test";
            settings["Cloudinary:ApiKey"] = "test";
            settings["Cloudinary:ApiSecret"] = "test";
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            "Testing";

        public string ApplicationName { get; set; } =
            "HudhudNestApi.Architecture.Tests";

        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
