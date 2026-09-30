using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HudhudNestApi.Infrastructure.Performance;

public static class PerformanceDatabaseDiagnosticsPolicy
{
    public const string ContextItemName = "HudhudNestApi.Performance.DatabaseDiagnostics";
    public const string CommandCountHeader = "X-Database-Command-Count";
    public const string DurationHeader = "X-Database-Duration-Ms";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
        => !environment.IsProduction()
           && configuration.GetValue<bool>("Performance:ExposeDatabaseDiagnostics");
}

public sealed class PerformanceDatabaseDiagnosticState
{
    private long _durationMicroseconds;
    private int _commandCount;

    public int CommandCount => Volatile.Read(ref _commandCount);
    public double DurationMilliseconds => Volatile.Read(ref _durationMicroseconds) / 1000d;

    public void Record(TimeSpan duration)
    {
        Interlocked.Increment(ref _commandCount);
        Interlocked.Add(ref _durationMicroseconds, (long)(duration.TotalMilliseconds * 1000d));
    }
}

public sealed class PerformanceDatabaseCommandInterceptor : DbCommandInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PerformanceDatabaseCommandInterceptor(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Record(eventData.Duration);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Record(eventData.Duration);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Record(eventData.Duration);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    private void Record(TimeSpan duration)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.Items[PerformanceDatabaseDiagnosticsPolicy.ContextItemName]
            is PerformanceDatabaseDiagnosticState state)
        {
            state.Record(duration);
        }
    }
}
