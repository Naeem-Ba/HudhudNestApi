using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HudhudNestApi.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that logs every Command/Query
/// with execution time. Runs AFTER validation, BEFORE the handler.
///
/// BUG FIX: Was declared as "internal interface LoggingBehavior {}" — completely wrong.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("[START] Handling {RequestName}", requestName);

        var sw = Stopwatch.StartNew();
        TResponse response;

        try
        {
            response = await next();
        }
        catch (Exception ex) when (IsExpectedBusinessOutcome(ex))
        {
            // A refused request (conflict, validation, not found, forbidden, domain rule) is the system working,
            // not a fault: logging it at Error filled the alerts with 409s (phase 14, D12).
            sw.Stop();
            _logger.LogWarning(
                "[REFUSED] {RequestName} refused after {Elapsed}ms: {Reason}",
                requestName,
                sw.ElapsedMilliseconds,
                ex.GetType().Name);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "[ERROR] {RequestName} failed after {Elapsed}ms",
                requestName,
                sw.ElapsedMilliseconds);
            throw;
        }

        sw.Stop();

        // Warn if any operation takes longer than 500ms
        if (sw.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning(
                "[SLOW] {RequestName} took {Elapsed}ms — consider optimization",
                requestName,
                sw.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogInformation(
                "[END] {RequestName} completed in {Elapsed}ms",
                requestName,
                sw.ElapsedMilliseconds);
        }

        return response;
    }

    private static bool IsExpectedBusinessOutcome(Exception ex)
        => ex is Common.Exceptions.ConflictException
            or Common.Exceptions.ValidationException
            or Common.Exceptions.NotFoundException
            or Common.Exceptions.ForbiddenException
            or Domain.Common.Exceptions.DomainException;
}
