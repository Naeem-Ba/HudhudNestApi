using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Observability;

public sealed class CorrelationIdMiddleware
{
    private const int MaxCorrelationIdLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private readonly HudhudNestApiObservabilityOptions _options;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger,
        IOptions<HudhudNestApiObservabilityOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var route = RequestTelemetryClassifier.Classify(context.Request);
        var correlationId = ResolveCorrelationId(context);
        var activity = Activity.Current ??
            HudhudNestApiTelemetry.ActivitySource.StartActivity(
                "HTTP request",
                ActivityKind.Server);

        activity?.SetTag("service.name", _options.ServiceName);
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("route_group", route.RouteGroup);
        activity?.SetTag("operation", route.Operation);

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[_options.CorrelationHeaderName] = correlationId;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = activity?.TraceId.ToString() ?? context.TraceIdentifier,
            ["SpanId"] = activity?.SpanId.ToString() ?? string.Empty,
            ["CorrelationId"] = correlationId,
            ["RouteGroup"] = route.RouteGroup,
            ["Operation"] = route.Operation
        });

        var started = Stopwatch.GetTimestamp();
        Exception? exception = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var statusCode = context.Response.StatusCode;
            var outcome = exception is not null || statusCode >= 500
                ? "failure"
                : "success";

            activity?.SetTag("http.response.status_code", statusCode);
            activity?.SetTag("outcome", outcome);

            HudhudNestApiTelemetry.RecordHttpRequest(
                route,
                context.Request.Method,
                statusCode,
                outcome,
                elapsedMilliseconds);

            if (_options.RequestLoggingEnabled)
            {
                _logger.LogInformation(
                    "HTTP request completed for {RouteGroup}/{Operation} with {StatusCode} in {ElapsedMilliseconds} ms.",
                    route.RouteGroup,
                    route.Operation,
                    statusCode,
                    Math.Round(elapsedMilliseconds, 2));
            }

            if (activity is not null && activity.Source == HudhudNestApiTelemetry.ActivitySource)
            {
                activity.Dispose();
            }
        }
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_options.CorrelationHeaderName, out var values))
        {
            var candidate = values.FirstOrDefault();

            if (IsSafeCorrelationId(candidate))
                return candidate!;
        }

        var currentTraceId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrWhiteSpace(currentTraceId)
            ? Guid.NewGuid().ToString("N")
            : currentTraceId;
    }

    private static bool IsSafeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCorrelationIdLength)
            return false;

        return value.All(static c =>
            char.IsLetterOrDigit(c) ||
            c is '-' or '_' or '.' or ':');
    }
}
