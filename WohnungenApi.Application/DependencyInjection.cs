using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WohnungenApi.Application.Common.Behaviors;

namespace WohnungenApi.Application;

/// <summary>
/// Extension method to register all Application layer services.
/// Call: builder.Services.AddApplication()
///
/// NEW FILE — was completely missing.
/// Without this, MediatR, validators, and pipeline behaviors are never registered,
/// so ALL Commands/Queries silently fail at runtime.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // ── MediatR ─────────────────────────────────────────────
        // Scans this assembly for all IRequestHandler<,> implementations
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Pipeline order: Logging → Validation → Handler
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // ── FluentValidation ─────────────────────────────────────
        // Scans for all AbstractValidator<T> in this assembly
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
