using System.Net.Http.Headers;
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

        // A request that authenticated through its Authorization: Bearer header carries no
        // ambient authority for CSRF to abuse: a browser never attaches that header on its own,
        // and a cross-site page cannot add it without a CORS preflight this API refuses.
        //
        // It also cannot satisfy the check below. The SPA fetches its token once, anonymously,
        // and antiforgery binds a request token to the authenticated user -- and
        // UseAuthentication runs before this middleware -- so every Bearer-authenticated unsafe
        // call (e.g. POST /api/auth/email/add, staging 2026-09-29) was rejected as "meant for a
        // different claims-based user". Requiring the token here protected nothing and broke
        // every such endpoint whenever the refresh_token cookie was present.
        //
        // Both halves must hold: the header alone is forgeable, but it is only trusted once
        // JwtBearer has actually accepted it (IsAuthenticated). JWT bearer is the only
        // authentication scheme, so an authenticated user here always came from that header.
        if (IsAuthenticatedViaBearerHeader(context))
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

    private static bool IsAuthenticatedViaBearerHeader(HttpContext context)
        => context.User.Identity?.IsAuthenticated == true &&
           AuthenticationHeaderValue.TryParse(
               context.Request.Headers.Authorization.ToString(),
               out var header) &&
           string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(header.Parameter);
}
