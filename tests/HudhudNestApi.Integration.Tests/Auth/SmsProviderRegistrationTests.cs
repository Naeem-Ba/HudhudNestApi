using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Phone;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Infrastructure.Auth.Otp;
using HudhudNestApi.Infrastructure.Auth;
using HudhudNestApi.Infrastructure.Auth.Services;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

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

    // ───────────── OTP channels (Telegram / WhatsApp) ─────────────

    private static Dictionary<string, string?> SmsSettings() => new()
    {
        ["SmsProvider:Provider"] = "D7",
        ["SmsProvider:ApiKey"] = "k",
        ["SmsProvider:FromNumber"] = "HudhudNest"
    };

    private static IOtpChannelService Channels(ServiceProvider services) =>
        services.CreateScope().ServiceProvider.GetRequiredService<IOtpChannelService>();

    [Fact]
    public void Production_WithoutTheChannelFlags_OnlySmsExists_AndTheHostStartsWithNoTelegramOrWhatsAppSettings()
    {
        using var services = Build(SmsSettings());

        var channels = Channels(services).Describe("+4915772378923");

        Assert.True(channels.Single(x => x.Channel == OtpChannel.Sms).Available);
        Assert.False(channels.Single(x => x.Channel == OtpChannel.Telegram).Available);
        Assert.False(channels.Single(x => x.Channel == OtpChannel.WhatsApp).Available);
    }

    [Fact]
    public void Production_TelegramEnabled_UsesTheRealGatewayProvider_AndNeedsAToken()
    {
        var settings = SmsSettings();
        settings["OtpChannels:Telegram:Enabled"] = "true";

        using (var withoutToken = Build(settings))
        {
            var failure = Assert.Throws<OptionsValidationException>(
                () => withoutToken.GetRequiredService<IOptions<TelegramGatewayOptions>>().Value);
            Assert.Contains("TelegramGateway:ApiToken", failure.Message);
        }

        settings["TelegramGateway:ApiToken"] = "token";
        using var withToken = Build(settings);
        Assert.Contains(withToken.CreateScope().ServiceProvider.GetServices<IOtpProvider>(), x => x is TelegramGatewayOtpProvider);
        Assert.True(Channels(withToken).IsAvailable(OtpChannel.Telegram, "+4915772378923"));
    }

    [Fact]
    public void Production_WhatsAppEnabled_NeedsItsCredentialsAndTemplate_AndIsRefusedForSyria()
    {
        var settings = SmsSettings();
        settings["OtpChannels:WhatsApp:Enabled"] = "true";

        using (var incomplete = Build(settings))
        {
            var failure = Assert.Throws<OptionsValidationException>(
                () => incomplete.GetRequiredService<IOptions<WhatsAppCloudOptions>>().Value);
            Assert.Contains("WhatsAppCloud:AccessToken", failure.Message);
        }

        settings["WhatsAppCloud:AccessToken"] = "t";
        settings["WhatsAppCloud:PhoneNumberId"] = "123";
        settings["WhatsAppCloud:TemplateName"] = "hudhud_otp";
        using var complete = Build(settings);
        Assert.Contains(complete.CreateScope().ServiceProvider.GetServices<IOtpProvider>(), x => x is WhatsAppCloudOtpProvider);
        Assert.True(Channels(complete).IsAvailable(OtpChannel.WhatsApp, "+4915772378923"));
        Assert.False(Channels(complete).IsAvailable(OtpChannel.WhatsApp, "+963944111222"));
    }

    [Fact]
    public void Production_ATelegramTokenOverPlainHttp_IsRefused()
    {
        var settings = SmsSettings();
        settings["OtpChannels:Telegram:Enabled"] = "true";
        settings["TelegramGateway:ApiToken"] = "token";
        settings["TelegramGateway:BaseUrl"] = "http://gatewayapi.telegram.org/";
        using var services = Build(settings);

        var failure = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<TelegramGatewayOptions>>().Value);
        Assert.Contains("HTTPS", failure.Message);
    }

    [Fact]
    public void Development_EnabledChannels_UseTheConsoleStandIn_SoNoCredentialIsNeeded()
    {
        var settings = new Dictionary<string, string?>
        {
            ["OtpChannels:Telegram:Enabled"] = "true",
            ["OtpChannels:WhatsApp:Enabled"] = "true"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        typeof(AuthInfrastructureRegistration)
            .GetMethod("AddSmsAndOtpServices", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [collection, configuration, new ProductionEnvironment { EnvironmentName = Environments.Development }]);
        using var services = collection.BuildServiceProvider();

        var providers = services.CreateScope().ServiceProvider.GetServices<IOtpProvider>().ToArray();

        Assert.Equal(2, providers.OfType<ConsoleOtpProvider>().Count());
        Assert.True(Channels(services).IsAvailable(OtpChannel.Telegram, "+963944111222"));
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
