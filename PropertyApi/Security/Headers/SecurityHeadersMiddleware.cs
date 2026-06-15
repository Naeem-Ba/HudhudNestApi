using Microsoft.Extensions.Options;

namespace PropertyApi.Security.Headers;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<SecurityHeadersOptions> _options;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        IOptions<SecurityHeadersOptions> options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _options.Value;

        if (options.Enabled)
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("X-Permitted-Cross-Domain-Policies", "none");
            headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
            headers.TryAdd("Cross-Origin-Resource-Policy", "same-origin");
            headers.TryAdd("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");

            if (!string.IsNullOrWhiteSpace(options.ContentSecurityPolicy) &&
                !context.Request.Path.StartsWithSegments("/swagger"))
            {
                headers.TryAdd("Content-Security-Policy", options.ContentSecurityPolicy);
            }
        }

        await _next(context);
    }
}
