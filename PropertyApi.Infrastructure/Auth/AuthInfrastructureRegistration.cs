using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Audit;
using PropertyApi.Infrastructure.Auth.Repositories;
using PropertyApi.Infrastructure.Auth.Security;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Infrastructure.Auth.Services;
using PropertyApi.Infrastructure.Email;
using PropertyApi.Infrastructure.Identity.Services;
using PropertyApi.Infrastructure.Lookups;
using PropertyApi.Infrastructure.Persistence.Backfills;
using PropertyApi.Infrastructure.Settings;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;

namespace PropertyApi.Infrastructure.Auth;

internal static class AuthInfrastructureRegistration
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        AddJwtOptions(
            services,
            configuration);

        AddIdentityCapabilities(
            services);

        AddSocialAuthentication(
            services,
            configuration);

        AddSecurityStampServices(
            services);

        AddAuthRepositoriesAndUtilities(
            services);

        AddEmailServices(
            services,
            configuration,
            environment);

        AddSmsAndOtpServices(
            services,
            configuration,
            environment);

        return services;
    }

    private static void AddJwtOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.Key) &&
                    options.Key.Length >= 32,
                "Jwt:Key must contain at least 32 characters.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Issuer),
                "Jwt:Issuer is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Audience),
                "Jwt:Audience is required.")
            .Validate(
                options => options.AccessTokenMinutes > 0,
                "Jwt:AccessTokenMinutes must be greater than zero.")
            .Validate(
                options => options.RefreshTokenDays > 0,
                "Jwt:RefreshTokenDays must be greater than zero.")
            .ValidateOnStart();
    }

    private static void AddIdentityCapabilities(
        IServiceCollection services)
    {
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();

        services.AddSingleton<
            IPhoneNumberLookupHasher,
            HmacPhoneNumberLookupHasher>();
        services.AddSingleton<IPhoneNumberNormalizer, E164PhoneNumberNormalizer>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPhoneAuthenticationWorkflow, PhoneAuthenticationWorkflow>();
        services.AddHostedService<PhoneVerificationHostedService>();

        services.AddScoped<IdentityAccountReader>();
        services.AddScoped<IdentityAccountCreator>();
        services.AddScoped<IdentityAccessService>();
        services.AddScoped<IdentityCredentialService>();
#pragma warning disable CS0618 // Compatibility facade is intentionally registered during staged migration.
        services.AddScoped<IIdentityCapabilityAdapter, PureIdentityService>();
#pragma warning restore CS0618

        services.AddScoped<ISocialLoginIdentityService>(
            sp => (ISocialLoginIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IAddEmailIdentityService>(
            sp => (IAddEmailIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IForgotPasswordIdentityService>(
            sp => (IForgotPasswordIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IRefreshTokenIdentityService>(
            sp => (IRefreshTokenIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IPhoneOtpIdentityService>(
            sp => (IPhoneOtpIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IRegisterIdentityService>(
            sp => (IRegisterIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<ILoginIdentityService>(
            sp => (ILoginIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IResetPasswordIdentityService>(
            sp => (IResetPasswordIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<ILogoutIdentityService>(
            sp => (ILogoutIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IVerifyEmailIdentityService>(
            sp => (IVerifyEmailIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IChangePasswordIdentityService>(
            sp => (IChangePasswordIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IUserIdentityReadService>(
            sp => (IUserIdentityReadService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IUpdateUserIdentityService>(
            sp => (IUpdateUserIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());
        services.AddScoped<IDeleteUserIdentityService>(
            sp => (IDeleteUserIdentityService)
                sp.GetRequiredService<IIdentityCapabilityAdapter>());

        services.AddScoped<IIdentityRoleService, IdentityRoleService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
    }

    private static void AddSocialAuthentication(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SocialAuthSettings>()
            .Bind(configuration.GetSection(SocialAuthSettings.SectionName));

        services.AddMemoryCache();
        services.AddScoped<ISocialTokenVerifier, GoogleTokenVerifier>();
        services.AddHttpClient<AppleTokenVerifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<ISocialTokenVerifier>(sp =>
            sp.GetRequiredService<AppleTokenVerifier>());
    }

    private static void AddSecurityStampServices(
        IServiceCollection services)
    {
        services.AddScoped<IUserSecurityStampReader, IdentitySecurityStampReader>();
        services.AddScoped<IUserSecurityStampValidator, CachedSecurityStampValidator>();
        services.AddScoped<IUserSecurityStampCacheInvalidator, DistributedSecurityStampCacheInvalidator>();
    }

    private static void AddAuthRepositoriesAndUtilities(
        IServiceCollection services)
    {
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetUrlBuilder, PasswordResetUrlBuilder>();
        services.AddScoped<IJwtTokenSettings, JwtTokenSettings>();
        services.AddHostedService<OtpCleanupHostedService>();
        services.AddScoped<PhoneNumberLookupHashBackfill>();
    }

    private static void AddEmailServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<EmailOptions>(
            configuration.GetSection(EmailOptions.SectionName));

        if (environment.IsProduction())
        {
            services.AddScoped<SmtpEmailSender>();

            services.AddScoped<IdentityEmailSender>(
                sp => sp.GetRequiredService<SmtpEmailSender>());

            services.AddScoped<IApplicationEmailSender>(
                sp => sp.GetRequiredService<SmtpEmailSender>());
        }
        else
        {
            services.AddScoped<ConsoleEmailSender>();

            services.AddScoped<IdentityEmailSender>(
                sp => sp.GetRequiredService<ConsoleEmailSender>());

            services.AddScoped<IApplicationEmailSender>(
                sp => sp.GetRequiredService<ConsoleEmailSender>());
        }
    }

    private static void AddSmsAndOtpServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<SmsProviderOptions>()
            .Bind(configuration.GetSection(SmsProviderOptions.SectionName));

        if (environment.IsProduction())
        {
            services.AddOptions<SmsProviderOptions>()
                .Bind(configuration.GetSection(SmsProviderOptions.SectionName))
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.Provider),
                    "SmsProvider:Provider is required in Production.")
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.ApiUrl),
                    "SmsProvider:ApiUrl is required in Production.")
                .Validate(
                    options =>
                        Uri.TryCreate(options.ApiUrl, UriKind.Absolute, out var uri) &&
                        uri.Scheme == Uri.UriSchemeHttps,
                    "SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production.")
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.ApiKey),
                    "SmsProvider:ApiKey is required in Production.")
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.FromNumber),
                    "SmsProvider:FromNumber is required in Production.")
                .ValidateOnStart();
        }

        services.AddScoped<IOtpCodeRepository, OtpCodeRepository>();

        var stagingTestSupportEnabled =
            StagingTestSupportPolicy.IsEnabled(environment, configuration);

        if (stagingTestSupportEnabled)
            services.AddScoped<IOtpService, StagingFixedOtpService>();
        else
            services.AddScoped<IOtpService, OtpService>();

        services.AddScoped<IEmailVerificationService, EmailVerificationService>();

        if (stagingTestSupportEnabled)
        {
            services.AddScoped<ISmsService, StagingSmokeSmsSink>();
        }
        else if (environment.IsDevelopment() ||
            environment.EnvironmentName == "Testing" ||
            environment.EnvironmentName == "CI")
        {
            services.AddScoped<ISmsService, ConsoleSmsService>();
        }
        else
        {
            var smsProvider =
                configuration["SmsProvider:Provider"];

            if (string.Equals(
                    smsProvider,
                    "Twilio",
                    StringComparison.OrdinalIgnoreCase))
            {
                services.AddScoped<ISmsService, TwilioSmsService>();
            }
            else
            {
                services.AddHttpClient<ISmsService, HttpSmsService>();
            }
        }
    }
}
