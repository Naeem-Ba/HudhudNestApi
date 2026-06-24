using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Behaviors;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Admin.Services;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Services;

namespace PropertyApi.Application;

/// <summary>
/// Extension method to register all Application layer services.
/// Call: builder.Services.AddApplication()
///
/// NEW FILE â€” was completely missing.
/// Without this, MediatR, validators, and pipeline behaviors are never registered,
/// so ALL Commands/Queries silently fail at runtime.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // â”€â”€ MediatR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Scans this assembly for all IRequestHandler<,> implementations
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Pipeline order: Logging â†’ Validation â†’ Handler
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // â”€â”€ FluentValidation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Scans for all AbstractValidator<T> in this assembly
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IPropertyOwnershipService, PropertyOwnershipService>();

        return services;
    }
}

