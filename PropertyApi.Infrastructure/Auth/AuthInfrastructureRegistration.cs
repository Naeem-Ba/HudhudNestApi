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
using PropertyApi.Infrastructure.Users;
using PropertyApi.Application.Users.Commands.DeleteUser;
using PropertyApi.Application.Users.Interfaces;
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
            services,
            configuration);

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
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ITokenService, TokenService>();

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

        services.AddScoped<SecurityAlertEmailService>();
        services.AddScoped<ISecurityAlertService>(
            sp => sp.GetRequiredService<SecurityAlertEmailService>());

        // One singleton serving two roles: the queue that request handlers write to, and
        // the hosted worker that drains it. Registered by instance so both resolve to the
        // same object -- AddHostedService<T>() alone would create a second one, and alerts
        // enqueued on the singleton would sit in a channel nobody reads.
        services.AddSingleton<SecurityAlertBackgroundService>();
        services.AddSingleton<ISecurityAlertDispatcher>(
            sp => sp.GetRequiredService<SecurityAlertBackgroundService>());
        services.AddHostedService(
            sp => sp.GetRequiredService<SecurityAlertBackgroundService>());
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
        services.AddScoped<IResendConfirmationIdentityService>(
            sp => (IResendConfirmationIdentityService)
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

        // Off by default (AuditLogRetention:Enabled) until an operator explicitly sets a
        // retention window — see AuditLogRetentionHostedService's own doc comment.
        services.AddHostedService<AuditLogRetentionHostedService>();

        // Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md) — always on, unlike audit
        // log retention, since executing a deletion the owner already explicitly requested (and
        // had the whole delay window to cancel) is not a new retention policy being invented.
        services.AddOptions<AccountDeletionOptions>()
            .Bind(configuration.GetSection(AccountDeletionOptions.SectionName));
        services.AddScoped<IAccountDeletionSettings, AccountDeletionSettings>();

        // Registered as itself (not only reachable via IRequestHandler<DeleteUserCommand,_>)
        // so AccountDeletionSweepHostedService can call ExecuteScheduledDeletionAsync directly
        // — that method is not part of the MediatR pipeline, since a background sweep has no
        // HTTP request to route through ISender.
        services.AddScoped<DeleteUserCommandHandler>();

        services.AddHostedService<AccountDeletionSweepHostedService>();
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
        services.AddScoped<IEmailConfirmationUrlBuilder, EmailConfirmationUrlBuilder>();
        services.AddScoped<IJwtTokenSettings, JwtTokenSettings>();
        services.AddScoped<PhoneNumberLookupHashBackfill>();
    }

    private const string ConsoleEmailProvider = "Console";
    private const string SmtpEmailProvider = "Smtp";
    private const string ResendEmailProvider = "Resend";

    private static void AddEmailServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var provider =
            ResolveEmailProvider(
                configuration,
                environment);

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                options =>
                    string.IsNullOrWhiteSpace(options.Provider) ||
                    IsKnownEmailProvider(options.Provider),
                "Email:Provider must be Console, Smtp or Resend.")
            .ValidateOnStart();

        switch (provider)
        {
            case ResendEmailProvider:
                AddResendEmailSender(
                    services,
                    configuration);
                break;

            case SmtpEmailProvider:
                AddAliasedEmailSender<SmtpEmailSender>(
                    services);
                break;

            default:
                // Logging a message to the console instead of delivering it is a
                // development convenience. Reaching Production with it selected means
                // nobody would ever receive a confirmation link, and nothing would say so
                // -- refuse to start rather than fail silently for the next release.
                if (environment.IsProduction())
                {
                    throw new InvalidOperationException(
                        "Email:Provider must be Smtp or Resend in Production; Console only logs messages.");
                }

                AddAliasedEmailSender<ConsoleEmailSender>(
                    services);
                break;
        }

        ValidateConfirmationLinkOrigin(
            configuration,
            environment);
    }

    /// <summary>
    /// Asks the URL builder its question at startup, while the answer can still stop a
    /// deployment.
    ///
    /// The confirmation link is otherwise built lazily, inside a send that both callers
    /// deliberately swallow. A Production instance configured with no trusted origin -- or
    /// with the plain-HTTP default that ships in appsettings.json -- would register
    /// accounts, log an error nobody is watching, and mail nothing at all.
    /// </summary>
    private static void ValidateConfirmationLinkOrigin(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        // Built rather than re-checked, so there is one set of rules and not two.
        _ = new EmailConfirmationUrlBuilder(configuration, environment)
            .Build(Guid.Empty, "startup-validation");
    }

    private static string ResolveEmailProvider(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configured =
            configuration[$"{EmailOptions.SectionName}:Provider"];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return NormalizeEmailProvider(configured);
        }

        // Unset, keep the rule this method used to apply on its own, so an environment
        // that has never heard of Email:Provider behaves exactly as it did before.
        return environment.IsProduction()
            ? SmtpEmailProvider
            : ConsoleEmailProvider;
    }

    private static bool IsKnownEmailProvider(
        string provider)
        => NormalizeEmailProvider(provider) is
            ConsoleEmailProvider or
            SmtpEmailProvider or
            ResendEmailProvider;

    private static string NormalizeEmailProvider(
        string provider)
    {
        var trimmed = provider.Trim();

        if (string.Equals(trimmed, ConsoleEmailProvider, StringComparison.OrdinalIgnoreCase))
            return ConsoleEmailProvider;

        if (string.Equals(trimmed, SmtpEmailProvider, StringComparison.OrdinalIgnoreCase))
            return SmtpEmailProvider;

        if (string.Equals(trimmed, ResendEmailProvider, StringComparison.OrdinalIgnoreCase))
            return ResendEmailProvider;

        return trimmed;
    }

    private static void AddResendEmailSender(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ResendEmailOptions>()
            .Bind(configuration.GetSection(ResendEmailOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Email:Resend:ApiKey is required when Email:Provider is Resend.")
            .Validate(
                options =>
                    Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps,
                "Email:Resend:BaseUrl must be a valid absolute HTTPS URL.")
            .Validate(
                options => options.TimeoutSeconds is > 0 and <= 120,
                "Email:Resend:TimeoutSeconds must be between 1 and 120.")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.From),
                "Email:From is required when Email:Provider is Resend.")
            .ValidateOnStart();

        var resendOptions =
            configuration
                .GetSection(ResendEmailOptions.SectionName)
                .Get<ResendEmailOptions>()
            ?? new ResendEmailOptions();

        services.AddHttpClient<ResendEmailSender>(client =>
        {
            client.BaseAddress =
                new Uri(
                    resendOptions.BaseUrl.TrimEnd('/') + "/");

            client.Timeout =
                TimeSpan.FromSeconds(resendOptions.TimeoutSeconds);
        });

        AddEmailSenderAliases<ResendEmailSender>(services);
    }

    private static void AddAliasedEmailSender<TSender>(
        IServiceCollection services)
        where TSender : class, IdentityEmailSender, IApplicationEmailSender
    {
        services.AddScoped<TSender>();

        AddEmailSenderAliases<TSender>(services);
    }

    /// <summary>
    /// Publishes one sender under both contracts.
    ///
    /// The aliases are registered Scoped even when the sender behind them is not:
    /// AddHttpClient registers its typed client Transient, and CompositionRegistrationTests
    /// pins IApplicationEmailSender to a scoped lifetime. Resolving through these factories
    /// keeps that promise whatever lifetime the concrete type happens to have.
    /// </summary>
    private static void AddEmailSenderAliases<TSender>(
        IServiceCollection services)
        where TSender : class, IdentityEmailSender, IApplicationEmailSender
    {
        services.AddScoped<IdentityEmailSender>(
            sp => sp.GetRequiredService<TSender>());

        services.AddScoped<IApplicationEmailSender>(
            sp => sp.GetRequiredService<TSender>());
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
                    options => !options.UsesSmsProviderKeys || !string.IsNullOrWhiteSpace(options.ResolveApiUrl()),
                    "SmsProvider:ApiUrl is required in Production.")
                .Validate(
                    options =>
                        !options.UsesSmsProviderKeys ||
                        (Uri.TryCreate(options.ResolveApiUrl(), UriKind.Absolute, out var uri) &&
                         uri.Scheme == Uri.UriSchemeHttps),
                    "SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production.")
                .Validate(
                    options => !options.UsesSmsProviderKeys || !string.IsNullOrWhiteSpace(options.ApiKey),
                    "SmsProvider:ApiKey is required in Production.")
                .Validate(
                    options => !options.RequiresFromNumber || !string.IsNullOrWhiteSpace(options.FromNumber),
                    "SmsProvider:FromNumber is required in Production.")
                .ValidateOnStart();
        }

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
            else if (string.Equals(smsProvider, "D7", StringComparison.OrdinalIgnoreCase))
            {
                services.AddHttpClient<ISmsService, D7SmsService>();
            }
            else if (string.Equals(smsProvider, "Unimatrix", StringComparison.OrdinalIgnoreCase))
            {
                // The AccessKey ID travels in the URL query, so no HttpClient request logging for this client.
                services.AddHttpClient<ISmsService, UnimatrixSmsService>().RemoveAllLoggers();
            }
            else
            {
                services.AddHttpClient<ISmsService, HttpSmsService>();
            }
        }
    }
}
