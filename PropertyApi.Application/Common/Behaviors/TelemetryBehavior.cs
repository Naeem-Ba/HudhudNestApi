using System.Diagnostics;
using MediatR;
using PropertyApi.Application.Common.Observability;

namespace PropertyApi.Application.Common.Behaviors;

public sealed class TelemetryBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var requestKind = ClassifyRequestKind(requestName);

        using var activity = ApplicationTelemetry.ActivitySource.StartActivity(
            $"Application {requestName}",
            ActivityKind.Internal);

        activity?.SetTag("application.request.name", requestName);
        activity?.SetTag("application.request.kind", requestKind);

        var started = Stopwatch.GetTimestamp();

        try
        {
            var response = await next();
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            activity?.SetTag("outcome", "success");
            ApplicationTelemetry.RecordRequest(
                requestKind,
                "success",
                elapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            activity?.SetTag("outcome", "failure");
            ApplicationTelemetry.RecordRequest(
                requestKind,
                "failure",
                elapsedMilliseconds);

            throw;
        }
    }

    private static string ClassifyRequestKind(string requestName)
    {
        if (requestName.EndsWith("Command", StringComparison.Ordinal))
            return "command";

        if (requestName.EndsWith("Query", StringComparison.Ordinal))
            return "query";

        return "request";
    }
}
