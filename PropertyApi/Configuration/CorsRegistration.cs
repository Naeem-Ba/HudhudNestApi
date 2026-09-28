using System.Text.RegularExpressions;

namespace PropertyApi.Configuration;

public static class CorsRegistration
{
    public static IServiceCollection AddPropertyApiCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool isTestingOrCi)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        // Opt-in only: unset (the default for every existing environment, including
        // Production) reproduces the exact previous WithOrigins(...) behavior below.
        // Lets an environment like Staging additionally trust origins that vary per
        // deploy and can't be listed as exact strings — e.g. Netlify's per-PR deploy
        // previews (https://deploy-preview-123--hudhudnest.netlify.app) — without
        // loosening Production's exact allow-list.
        var allowedOriginPatterns = configuration
            .GetSection("Cors:AllowedOriginPatterns")
            .Get<string[]>() ?? Array.Empty<string>();
        var compiledOriginPatterns = allowedOriginPatterns
            .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant))
            .ToArray();

        services.AddCors(options =>
        {
            options.AddPolicy("DefaultCors", policy =>
            {
                // RELEASE-BLOCKERS-AR.md B-13: the refresh-token cookie needs a credentialed CORS
                // policy to ever reach the browser — AllowAnyOrigin() and AllowCredentials() are
                // mutually exclusive by the CORS spec itself (browsers reject the combination
                // outright), so every environment with a configured origin list now gets the
                // credentialed policy, not just Production. This is not new risk: Development's
                // appsettings.Development.example.json and Testing's appsettings.Testing.json both
                // already list the real frontend origin(s).
                if (compiledOriginPatterns.Length > 0)
                {
                    var allowedOriginsSet = new HashSet<string>(allowedOrigins, StringComparer.Ordinal);
                    policy.SetIsOriginAllowed(origin =>
                              allowedOriginsSet.Contains(origin) ||
                              compiledOriginPatterns.Any(pattern => pattern.IsMatch(origin)))
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                }
                else if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                }
                else if (environment.IsDevelopment() || isTestingOrCi)
                {
                    // No origins configured at all (e.g. a fresh clone before appsettings.Development
                    // is filled in) — permissive fallback so the API still starts. Note this
                    // combination cannot carry the refresh-token cookie; see RefreshTokenCookie.
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                }
                else
                {
                    throw new InvalidOperationException("Cors:AllowedOrigins is required outside Development.");
                }
            });
        });

        return services;
    }
}
