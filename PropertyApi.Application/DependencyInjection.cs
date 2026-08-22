using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Behaviors;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Admin.Services;
using PropertyApi.Application.Auth.Commands.SocialLogin;
using PropertyApi.Application.Auth.Commands.RefreshToken;
using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Orchestration;
using PropertyApi.Application.Auth.Policies;
using PropertyApi.Application.Auth.Services;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Services;

namespace PropertyApi.Application;

/// <summary>
/// Extension method to register all Application layer services.
/// Call: builder.Services.AddApplication()
///
/// NEW FILE - was completely missing.
/// Without this, MediatR, validators, and pipeline behaviors are never registered,
/// so ALL Commands/Queries silently fail at runtime.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // MediatR
        // Scans this assembly for all IRequestHandler<,> implementations
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Pipeline order: Telemetry -> Logging -> Validation -> Handler
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TelemetryBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // FluentValidation
        // Scans for all AbstractValidator<T> in this assembly
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<RefreshTokenReuseHandler>();
        services.AddScoped<IAuthenticationSessionIssuer, AuthenticationSessionIssuer>();
        services.AddScoped<SocialIdentityValidator>();
        services.AddScoped<SocialAccountResolver>();
        services.AddScoped<SocialAccountMutationCoordinator>();
        services.AddScoped<ISocialAuthenticationOrchestrator, SocialAuthenticationOrchestrator>();
        services.AddSingleton<SocialAccountLinkingPolicy>();
        services.AddSingleton<SocialAccountCreationPolicy>();
        services.AddScoped<OtpConsumptionService>();
        services.AddScoped<PhoneAccountMutationCoordinator>();
        services.AddScoped<IPhoneOtpAuthenticationOrchestrator, PhoneOtpAuthenticationOrchestrator>();
        services.AddSingleton<PhoneOwnershipPolicy>();
        services.AddSingleton<IPhoneVerificationPolicy, PhoneVerificationPolicy>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IPropertyOwnershipService, PropertyOwnershipService>();

        // Password security service.
        // Validates password complexity, rejects common patterns locally, and screens
        // against Have I Been Pwned.
        //
        // The breaker is a singleton on purpose: it counts consecutive failures across
        // requests, which a scoped instance could never see.
        services.AddSingleton<PwnedPasswordsCircuitBreaker>();

        // Was `new HttpClient(...)` inside a scoped factory: one client per request
        // scope, never disposed, so sockets accumulated in TIME_WAIT and DNS changes
        // were never picked up. AddHttpClient pools the handler -- the same pattern
        // already used for AppleTokenVerifier, HttpSmsService and Cloudinary.
        services.AddHttpClient<IPasswordSecurityService, PasswordSecurityService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Add("User-Agent", "PropertyApi-PasswordValidator/1.0");
        });

        return services;
    }
}

