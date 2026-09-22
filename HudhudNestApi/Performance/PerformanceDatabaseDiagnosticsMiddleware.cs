using System.Globalization;
using HudhudNestApi.Infrastructure.Performance;

namespace HudhudNestApi.Performance;

public sealed class PerformanceDatabaseDiagnosticsMiddleware
{
    private readonly RequestDelegate _next;

    public PerformanceDatabaseDiagnosticsMiddleware(RequestDelegate next)
        => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var state = new PerformanceDatabaseDiagnosticState();
        context.Items[PerformanceDatabaseDiagnosticsPolicy.ContextItemName] = state;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[PerformanceDatabaseDiagnosticsPolicy.CommandCountHeader] =
                state.CommandCount.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers[PerformanceDatabaseDiagnosticsPolicy.DurationHeader] =
                state.DurationMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class PerformanceDatabaseDiagnosticsApplicationBuilderExtensions
{
    public static IApplicationBuilder UsePerformanceDatabaseDiagnostics(
        this IApplicationBuilder app,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (PerformanceDatabaseDiagnosticsPolicy.IsEnabled(environment, configuration))
            app.UseMiddleware<PerformanceDatabaseDiagnosticsMiddleware>();
        return app;
    }
}
