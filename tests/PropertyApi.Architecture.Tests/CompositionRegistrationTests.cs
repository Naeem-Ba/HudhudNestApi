using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Admin.Interfaces;
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
        AssertScoped<IOtpCodeRepository>(services);
        AssertScoped<IOtpService>(services);
        AssertScoped<IEmailVerificationService>(services);
        AssertScoped<IApplicationEmailSender>(services);
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

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(
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
                })
            .Build();

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
