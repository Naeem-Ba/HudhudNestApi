using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Middleware;

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
            // 422 � fluentvalidation errors from ValidationBehavior
            _logger.LogWarning("Validation errors: {@Errors}", ex.Errors);

            await WriteJson(context, StatusCodes.Status422UnprocessableEntity, new
            {
                title = "Validation Error",
                status = StatusCodes.Status422UnprocessableEntity,
                errors = ex.Errors,

                // Same shape as "errors", aligned index-for-index, carrying the stable
                // code for each message so a localised client can translate rather than
                // print the server's English.
                errorCodes = ex.ErrorCodes
            });
        }
        catch (FluentValidation.ValidationException ex)
        {
            // Safety net. ValidationBehavior normally converts FluentValidation
            // results into the application's own ValidationException above, so
            // reaching here means a validator threw from inside a rule instead
            // of reporting through the context (see RegisterCommandValidator).
            // Without this catch such a throw became an opaque HTTP 500 — the
            // caller lost the reason their input was rejected.
            _logger.LogWarning(
                "A validator threw FluentValidation.ValidationException instead of reporting " +
                "failures through the validation context for {Method} {Path}. Errors: {@Errors}",
                context.Request.Method,
                context.Request.Path,
                ex.Errors.Select(failure => failure.ErrorMessage));

            var grouped = ex.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToList();

            var errors = grouped.ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());

            var errorCodes = grouped.ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorCode ?? string.Empty).ToArray());

            await WriteJson(context, StatusCodes.Status422UnprocessableEntity, new
            {
                title = "Validation Error",
                status = StatusCodes.Status422UnprocessableEntity,
                errors,
                errorCodes
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogInformation(
                "Resource not found: {Message}",
                ex.Message);

            await WriteJson(context, StatusCodes.Status404NotFound, new
            {
                title = "Not Found",
                status = StatusCodes.Status404NotFound,
                message = ex.Message
            });
        }
        catch (ConflictException ex)
        {
            _logger.LogInformation(
                "Conflict: {Message}",
                ex.Message);

            await WriteJson(context, StatusCodes.Status409Conflict, new
            {
                title = "Conflict",
                status = StatusCodes.Status409Conflict,
                message = ex.Message
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // RELEASE-BLOCKERS-AR.md B-9: Property now carries an xmin concurrency token, so
            // two writers racing the same row raise this instead of silently letting the
            // second SaveChangesAsync overwrite the first. Without this catch, that race
            // surfaced as an unhandled 500 — trading silent data loss for a crash is not the
            // fix B-9 asked for; the caller needs a 409 it can react to (reload and retry).
            _logger.LogInformation(
                ex,
                "Concurrency conflict for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            await WriteJson(context, StatusCodes.Status409Conflict, new
            {
                title = "Conflict",
                status = StatusCodes.Status409Conflict,
                message = "تم تعديل هذا العنصر من قبل مستخدم أو عملية أخرى في نفس اللحظة. " +
                    "أعد تحميل البيانات وحاول مرة أخرى."
            });
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning(
                "Forbidden operation: {Message}",
                ex.Message);

            await WriteJson(context, StatusCodes.Status403Forbidden, new
            {
                title = "Forbidden",
                status = StatusCodes.Status403Forbidden,
                message = ex.Message,

                // Lets a localised client translate rather than print the server's
                // English — same idea as ValidationException.ErrorCodes above. Null
                // for the (older) call sites that never set one.
                code = ex.Code
            });
        }
        catch (DomainException ex)
        {
            // 400 � business rule violation from Domain layer
            _logger.LogWarning("Domain error: {Message}", ex.Message);

            await WriteJson(context, StatusCodes.Status400BadRequest, new
            {
                title = "Business Rule Violation",
                status = StatusCodes.Status400BadRequest,
                message = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            // 403 � ownership check failed in handler
            _logger.LogWarning(
                "Unauthorized access: {Message}",
                ex.Message);

            await WriteJson(context, StatusCodes.Status403Forbidden, new
            {
                title = "Forbidden",
                status = StatusCodes.Status403Forbidden,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            var shouldExposeDetails =
                _env.IsDevelopment() ||
                _env.EnvironmentName == "Testing" ||
                _env.EnvironmentName == "CI";

            object body = shouldExposeDetails
                ? new
                {
                    title = "Internal Server Error",
                    status = StatusCodes.Status500InternalServerError,
                    message = ex.Message,
                    exception = ex.GetType().FullName,
                    detail = ex.StackTrace,
                    innerException = ex.InnerException?.Message,
                    innerExceptionType = ex.InnerException?.GetType().FullName
                }
                : new
                {
                    title = "Internal Server Error",
                    status = StatusCodes.Status500InternalServerError,
                    message = "An unexpected error occurred. Please try again later."
                };

            await WriteJson(context, StatusCodes.Status500InternalServerError, body);
        }
    }

    private static Task WriteJson(HttpContext ctx, int statusCode, object body)
    {
        if (ctx.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";

        return ctx.Response.WriteAsync(
            JsonSerializer.Serialize(body, _jsonOptions));
    }
}