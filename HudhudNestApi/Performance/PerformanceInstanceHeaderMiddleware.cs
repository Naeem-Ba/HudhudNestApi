using System.Text.RegularExpressions;

namespace HudhudNestApi.Performance;

public static partial class PerformanceInstanceHeaderPolicy
{
    public const string HeaderName = "X-Instance-Id";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
        => !environment.IsProduction()
           && configuration.GetValue<bool>("Performance:ExposeInstanceId");

    public static string GetValidatedInstanceId(IConfiguration configuration)
    {
        var instanceId = configuration["Performance:InstanceId"]?.Trim();
        if (string.IsNullOrWhiteSpace(instanceId) || !SafeInstanceId().IsMatch(instanceId))
        {
            throw new InvalidOperationException(
                "Performance:InstanceId must contain 1-64 letters, digits, dots, underscores, or hyphens.");
        }

        return instanceId;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeInstanceId();
}

public sealed class PerformanceInstanceHeaderMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _instanceId;

    public PerformanceInstanceHeaderMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _instanceId = PerformanceInstanceHeaderPolicy.GetValidatedInstanceId(configuration);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[PerformanceInstanceHeaderPolicy.HeaderName] = _instanceId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class PerformanceInstanceHeaderApplicationBuilderExtensions
{
    public static IApplicationBuilder UsePerformanceInstanceHeader(
        this IApplicationBuilder app,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (PerformanceInstanceHeaderPolicy.IsEnabled(environment, configuration))
        {
            app.UseMiddleware<PerformanceInstanceHeaderMiddleware>();
        }

        return app;
    }
}
