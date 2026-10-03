namespace HudhudNestApi.Health;

/// <summary>
/// Serves <c>/health/ready</c> from a short-lived copy of the last real answer.
///
/// The endpoint has to stay anonymous (Render, the release gates, rollback and the smoke tests all poll
/// it without credentials), but every call runs a PostgreSQL/PostGIS probe and a Redis round trip. Left
/// as is, that is a free way for anyone to put load on both backends (security audit 2026-10-03, F-08).
/// Caching the response bounds that to one probe per <see cref="Ttl"/> per instance, however many
/// requests arrive, and a 503 is cached just like a 200 so the answer never flaps inside the window.
/// </summary>
public sealed class ReadinessResponseCacheMiddleware
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private readonly RequestDelegate _next;
    private readonly TimeProvider _clock;
    private CachedResponse? _cached;

    public ReadinessResponseCacheMiddleware(RequestDelegate next, TimeProvider clock)
    {
        _next = next;
        _clock = clock;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var now = _clock.GetUtcNow();
        var cached = _cached;

        if (cached is not null && now - cached.At < Ttl)
        {
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = cached.ContentType;
            await context.Response.Body.WriteAsync(cached.Body, context.RequestAborted);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = original;
        }

        var body = buffer.ToArray();
        _cached = new CachedResponse(now, context.Response.StatusCode, context.Response.ContentType, body);
        await original.WriteAsync(body, context.RequestAborted);
    }

    private sealed record CachedResponse(DateTimeOffset At, int StatusCode, string? ContentType, byte[] Body);
}
