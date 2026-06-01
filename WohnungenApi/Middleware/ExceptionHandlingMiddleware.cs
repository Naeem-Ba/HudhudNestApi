using System.Text.Json;
using WohnungenApi.Application.Common.Exceptions;
using WohnungenApi.Domain.Common.Exceptions;

namespace WohnungenApi.Middleware;

/// <summary>
/// Centralized exception handler middleware.
/// Catches all unhandled exceptions and returns structured JSON responses.
///
/// NEW FILE: Replaces the inline lambda in Program.cs which had issues:
/// 1. app.UseDeveloperExceptionPage() was called in Production (security risk!)
/// 2. Access-Control-Allow-Origin: * manually added (bypasses CORS policy)
/// 3. No structured error format
///
/// Register in Program.cs BEFORE all other middleware:
///   app.UseMiddleware&lt;ExceptionHandlingMiddleware&gt;();
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            // 422 — fluentvalidation errors from ValidationBehavior
            _logger.LogWarning("Validation errors: {@Errors}", ex.Errors);
            await WriteJson(context, 422, new
            {
                title = "Validation Error",
                status = 422,
                errors = ex.Errors
            });
        }
        catch (DomainException ex)
        {
            // 400 — business rule violation from Domain layer
            _logger.LogWarning("Domain error: {Message}", ex.Message);
            await WriteJson(context, 400, new
            {
                title = "Business Rule Violation",
                status = 400,
                message = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            // 403 — ownership check failed in handler
            await WriteJson(context, 403, new
            {
                title = "Forbidden",
                status = 403,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Path}", context.Request.Path);

            // In Development: expose full details. In Production: safe generic message.
            object body = _env.IsDevelopment()
                ? new
                {
                    title = "Internal Server Error",
                    status = 500,
                    message = ex.Message,
                    detail = ex.StackTrace,
                    innerException = ex.InnerException?.Message
                }
                : (object)new
                {
                    title = "Internal Server Error",
                    status = 500,
                    message = "An unexpected error occurred. Please try again later."
                };

            await WriteJson(context, 500, body);
        }
    }

    private static Task WriteJson(HttpContext ctx, int statusCode, object body)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(
            JsonSerializer.Serialize(body, _jsonOptions));
    }
}