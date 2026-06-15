using Microsoft.AspNetCore.Http;

namespace PropertyApi.Security.Csrf;

public static class CsrfExtensions
{
    public static IServiceCollection AddPropertyApiAntiforgery(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<CookieCsrfOptions>(
            configuration.GetSection(CookieCsrfOptions.SectionName));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "XSRF-TOKEN";
            options.Cookie.HttpOnly = false;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        return services;
    }

    public static IApplicationBuilder UseCookieCsrfProtection(this IApplicationBuilder app)
        => app.UseMiddleware<CookieCsrfProtectionMiddleware>();
}
