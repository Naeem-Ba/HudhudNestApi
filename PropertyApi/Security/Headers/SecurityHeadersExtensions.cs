namespace PropertyApi.Security.Headers;

public static class SecurityHeadersExtensions
{
    public static IServiceCollection AddPropertyApiSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SecurityHeadersOptions>()
            .Bind(configuration.GetSection(SecurityHeadersOptions.SectionName))
            .ValidateOnStart();

        return services;
    }

    public static IApplicationBuilder UsePropertyApiSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
