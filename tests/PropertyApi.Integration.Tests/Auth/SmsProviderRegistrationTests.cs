using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Infrastructure.Auth;
using PropertyApi.Infrastructure.Auth.Services;
using Xunit;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// The provider switch and the Production option rules in AuthInfrastructureRegistration decide which SMS
/// adapter a deployed host uses and whether it starts at all; the adapters' own tests never reach them.
/// Drives the real (private) registration step with Production configuration.
/// </summary>
public sealed class SmsProviderRegistrationTests
{
    [Theory]
    [InlineData("D7", typeof(D7SmsService))]
    [InlineData("Unimatrix", typeof(UnimatrixSmsService))]
    public void Production_ResolvesTheConfiguredProvider(string provider, Type expected)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SmsProvider:Provider"] = provider,
            ["SmsProvider:ApiKey"] = "test-key",
            ["SmsProvider:FromNumber"] = provider == "D7" ? "HudhudNest" : null
        };

        using var services = Build(settings);

        Assert.IsType(expected, services.CreateScope().ServiceProvider.GetRequiredService<ISmsService>());
    }

    [Fact]
    public void Production_Unimatrix_NeedsNoFromNumber_ButD7Does()
    {
        using var unimatrix = Build(new() { ["SmsProvider:Provider"] = "Unimatrix", ["SmsProvider:ApiKey"] = "k" });
        Assert.NotNull(unimatrix.GetRequiredService<IOptions<SmsProviderOptions>>().Value);

        using var d7 = Build(new() { ["SmsProvider:Provider"] = "D7", ["SmsProvider:ApiKey"] = "k" });
        var failure = Assert.Throws<OptionsValidationException>(
            () => d7.GetRequiredService<IOptions<SmsProviderOptions>>().Value);
        Assert.Contains("FromNumber", failure.Message);
    }

    [Fact]
    public void Production_MissingApiKey_FailsValidation()
    {
        using var services = Build(new() { ["SmsProvider:Provider"] = "D7", ["SmsProvider:FromNumber"] = "HudhudNest" });

        var failure = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<SmsProviderOptions>>().Value);
        Assert.Contains("ApiKey", failure.Message);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        typeof(AuthInfrastructureRegistration)
            .GetMethod("AddSmsAndOtpServices", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [services, configuration, new ProductionEnvironment()]);

        return services.BuildServiceProvider();
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = nameof(SmsProviderRegistrationTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
