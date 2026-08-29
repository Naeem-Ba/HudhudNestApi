using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PropertyApi.Application;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Auth.Commands.Register;
using PropertyApi.Application.Analytics.Interfaces;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Contact.Interfaces;
using PropertyApi.Application.Favorites.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Infrastructure;
using PropertyApi.Infrastructure.Email;
using PropertyApi.Infrastructure.Health;
using PropertyApi.Infrastructure.Identity.Services;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Architecture.Tests;

public sealed class CompositionRegistrationTests
{
    [Fact]
    public void AddInfrastructure_registers_auth_identity_capabilities_with_scoped_lifetimes()
    {
        var services =
            new ServiceCollection();

        var configuration =
            CreateConfiguration();

        services.AddInfrastructure(
            configuration,
            new TestHostEnvironment());

        AssertScoped<IIdentityCapabilityAdapter>(services);
        AssertScoped<ISocialLoginIdentityService>(services);
        AssertScoped<IAddEmailIdentityService>(services);
        AssertScoped<IForgotPasswordIdentityService>(services);
        AssertScoped<IRefreshTokenIdentityService>(services);
        AssertScoped<IPhoneOtpIdentityService>(services);
        AssertScoped<IRegisterIdentityService>(services);
        AssertScoped<ILoginIdentityService>(services);
        AssertScoped<IResetPasswordIdentityService>(services);
        AssertScoped<ILogoutIdentityService>(services);
        AssertScoped<IVerifyEmailIdentityService>(services);
        AssertScoped<IChangePasswordIdentityService>(services);
        AssertScoped<IUserIdentityReadService>(services);
        AssertScoped<IUpdateUserIdentityService>(services);
        AssertScoped<IDeleteUserIdentityService>(services);
        AssertScoped<IRefreshTokenRepository>(services);
        AssertScoped<IOtpService>(services);
        AssertScoped<IEmailVerificationService>(services);
        AssertScoped<IApplicationEmailSender>(services);
        AssertScoped<IEmailConfirmationUrlBuilder>(services);
        AssertScoped<IResendConfirmationIdentityService>(services);
        AssertScoped<ISmsService>(services);

        Assert.DoesNotContain(
            services,
            descriptor =>
                descriptor.ServiceType.Name.Equals(
                "IPureIdentityService",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void AddInfrastructure_registers_infrastructure_capabilities_with_expected_lifetimes()
    {
        var services =
            new ServiceCollection();

        var configuration =
            CreateConfiguration();

        services.AddInfrastructure(
            configuration,
            new TestHostEnvironment());

        AssertScoped<AppDbContext>(services);
        AssertScoped<IUnitOfWork>(services);

        AssertScoped<INotificationRepository>(services);
        AssertScoped<INotificationService>(services);
        AssertScoped<IAdminUserQueryRepository>(services);
        AssertScoped<IAdminIdentityService>(services);
        AssertScoped<IPropertyGeoSearchRepository>(services);
        AssertScoped<IAnalyticsReadRepository>(services);
        AssertScoped<IPropertyReadRepository>(services);
        AssertScoped<IVisitRepository>(services);
        AssertScoped<IPropertyReviewRepository>(services);
        AssertScoped<IPropertyRepository>(services);
        AssertScoped<IPropertyImageRepository>(services);
        AssertScoped<IFavoriteRepository>(services);
        AssertScoped<IContactMessageRepository>(services);
        AssertScoped<IUserDirectoryReadService>(services);
        AssertScoped<IUserAccountRepository>(services);
        AssertScoped<IMessageRepository>(services);
        AssertScoped<ICommonLookupService>(services);

        AssertScoped<ICurrentUserService>(services);
        AssertScoped<IMediaStorageService>(services);
        AssertScoped<PostGisHealthCheck>(services);
        AssertSingleton<IDistributedCache>(services);
        AssertHostedService<ProductionStartupValidator>(services);
    }

    [Fact]
    public void Register_handler_resolves_from_the_real_container()
    {
        // Every other test in this class reads service descriptors; none of them
        // ever constructs anything. The handler unit tests are no help either --
        // they hand RegisterCommandHandler its dependencies directly, so they stay
        // green whatever the container does or does not know.
        //
        // That leaves one question unasked: after a dependency is added to a
        // handler, can the container still build it? This asks it. A registration
        // missed while wiring up a new collaborator fails here, at build time,
        // instead of as a 500 on somebody's first sign-up.
        var services =
            new ServiceCollection();

        var configuration =
            CreateConfiguration();

        var environment =
            new TestHostEnvironment();

        // WebApplicationBuilder registers all three of these for the real host; a bare
        // ServiceCollection does not, and services that take IConfiguration, an
        // ILogger or IHostEnvironment cannot be activated without them.
        services.AddLogging();

        services.AddSingleton(configuration);

        services.AddSingleton<IHostEnvironment>(environment);

        services.AddApplication();

        services.AddInfrastructure(
            configuration,
            environment);

        using var provider =
            services.BuildServiceProvider();

        using var scope =
            provider.CreateScope();

        var handler =
            scope.ServiceProvider
                .GetRequiredService<
                    IRequestHandler<RegisterCommand, RegisterResult>>();

        Assert.IsType<RegisterCommandHandler>(handler);
    }

    [Fact]
    public void Email_provider_switch_selects_the_configured_sender()
    {
        // AddHttpClient registers its typed client Transient. The alias registrations are
        // what keep IApplicationEmailSender scoped, as the test above insists it must be;
        // without them, selecting Resend would quietly change the lifetime of a service
        // every request handler resolves.
        var services =
            new ServiceCollection();

        services.AddLogging();

        var configuration =
            CreateConfiguration(
                new Dictionary<string, string?>
                {
                    ["Email:Provider"] = "Resend",
                    ["Email:From"] = "no-reply@example.com",
                    ["Email:Resend:ApiKey"] = "resend-test-key"
                });

        services.AddSingleton(configuration);

        services.AddInfrastructure(
            configuration,
            new TestHostEnvironment());

        AssertScoped<IApplicationEmailSender>(services);

        using var provider =
            services.BuildServiceProvider();

        using var scope =
            provider.CreateScope();

        Assert.IsType<ResendEmailSender>(
            scope.ServiceProvider
                .GetRequiredService<IApplicationEmailSender>());
    }

    [Fact]
    public void Console_email_provider_is_refused_in_production()
    {
        // Console only writes messages to the log. Reaching Production with it selected
        // means nobody ever receives a confirmation link and nothing says so, which is
        // exactly how the old IsProduction-only rule failed when SMTP was left unset.
        var services =
            new ServiceCollection();

        var configuration =
            CreateConfiguration(
                new Dictionary<string, string?>
                {
                    ["Email:Provider"] = "Console"
                });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(
                configuration,
                new TestHostEnvironment { EnvironmentName = "Production" }));

        Assert.Contains(
            "Email:Provider",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_is_refused_without_a_trusted_confirmation_link_origin()
    {
        // The send that builds this link is swallowed by both of its callers, so a bad
        // origin would otherwise surface as accounts that register fine and confirmation
        // emails that never arrive.
        var services =
            new ServiceCollection();

        var configuration =
            CreateConfiguration(
                new Dictionary<string, string?>
                {
                    ["Email:Provider"] = "Resend",
                    ["Email:From"] = "no-reply@example.com",
                    ["Email:Resend:ApiKey"] = "resend-test-key"
                });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(
                configuration,
                new TestHostEnvironment { EnvironmentName = "Production" }));

        Assert.Contains(
            "Frontend:BaseUrl",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_rejects_a_plain_http_confirmation_link_origin()
    {
        var services =
            new ServiceCollection();

        var configuration =
            CreateConfiguration(
                new Dictionary<string, string?>
                {
                    ["Email:Provider"] = "Resend",
                    ["Email:From"] = "no-reply@example.com",
                    ["Email:Resend:ApiKey"] = "resend-test-key",

                    // The value appsettings.json ships. Inheriting it in Production is the
                    // mistake worth catching.
                    ["Frontend:BaseUrl"] = "http://localhost:4200"
                });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(
                configuration,
                new TestHostEnvironment { EnvironmentName = "Production" }));

        Assert.Contains(
            "HTTPS",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resend_provider_requires_an_api_key()
    {
        var services =
            new ServiceCollection();

        var configuration =
            CreateConfiguration(
                new Dictionary<string, string?>
                {
                    ["Email:Provider"] = "Resend",
                    ["Email:From"] = "no-reply@example.com"
                });

        services.AddInfrastructure(
            configuration,
            new TestHostEnvironment());

        using var provider =
            services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ResendEmailOptions>>().Value);

        Assert.Contains(
            "Email:Resend:ApiKey",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static void AssertScoped<TService>(
        IEnumerable<ServiceDescriptor> services)
    {
        var descriptors =
            services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(TService))
                .ToArray();

        Assert.NotEmpty(descriptors);

        Assert.All(
            descriptors,
            descriptor =>
                Assert.Equal(
                    ServiceLifetime.Scoped,
                    descriptor.Lifetime));
    }

    private static void AssertSingleton<TService>(
        IEnumerable<ServiceDescriptor> services)
    {
        var descriptors =
            services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(TService))
                .ToArray();

        Assert.NotEmpty(descriptors);

        Assert.All(
            descriptors,
            descriptor =>
                Assert.Equal(
                    ServiceLifetime.Singleton,
                    descriptor.Lifetime));
    }

    private static void AssertHostedService<THostedService>(
        IEnumerable<ServiceDescriptor> services)
    {
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(THostedService) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    private static IConfiguration CreateConfiguration(
        IDictionary<string, string?>? overrides = null)
    {
        var settings =
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=propertyapi;Username=test;Password=test",

                ["Jwt:Key"] =
                    "0123456789abcdef0123456789abcdef",

                ["Jwt:Issuer"] =
                    "PropertyApi.Tests",

                ["Jwt:Audience"] =
                    "PropertyApi.Tests",

                ["Jwt:AccessTokenMinutes"] =
                    "30",

                ["Jwt:RefreshTokenDays"] =
                    "7",

                ["OtpSettings:SecretKey"] =
                    "integration-test-otp-secret-key-at-least-32-bytes",

                ["Security:PhoneLookupHmacKey"] =
                    "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="
            };

        if (overrides is not null)
        {
            foreach (var setting in overrides)
            {
                settings[setting.Key] = setting.Value;
            }
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
            "PropertyApi.Architecture.Tests";

        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
