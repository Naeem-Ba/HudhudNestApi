using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Security.Csrf;

public sealed class CookieCsrfProtectionMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Trace
    };

    private readonly RequestDelegate _next;
    private readonly IAntiforgery _antiforgery;
    private readonly IOptions<CookieCsrfOptions> _options;

    public CookieCsrfProtectionMiddleware(
        RequestDelegate next,
        IAntiforgery antiforgery,
        IOptions<CookieCsrfOptions> options)
    {
        _next = next;
        _antiforgery = antiforgery;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _options.Value;

        if (!options.Enabled || SafeMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IgnoreAntiforgeryTokenAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        var hasAuthCookie = context.Request.Cookies.ContainsKey(options.AuthenticationCookieName);
        var isAnonymousEndpoint = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;

        if (!hasAuthCookie && (!options.ProtectAnonymousUnsafeEndpoints || isAnonymousEndpoint))
        {
            await _next(context);
            return;
        }

        await _antiforgery.ValidateRequestAsync(context);
        await _next(context);
    }
}
