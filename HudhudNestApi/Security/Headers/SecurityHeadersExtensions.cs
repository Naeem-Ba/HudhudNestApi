namespace HudhudNestApi.Security.Headers;

public static class SecurityHeadersExtensions
{
    public static IServiceCollection AddHudhudNestApiSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SecurityHeadersOptions>()
            .Bind(configuration.GetSection(SecurityHeadersOptions.SectionName))
            .ValidateOnStart();

        return services;
    }

    public static IApplicationBuilder UseHudhudNestApiSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
