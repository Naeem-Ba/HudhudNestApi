using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace HudhudNestApi.Health;

public static class HealthEndpointExtensions
{
    public static IEndpointRouteBuilder MapOperationalHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthCheckResponseWriter.WriteMinimalAsync
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteMinimalAsync
        }).AllowAnonymous();

        return endpoints;
    }
}
