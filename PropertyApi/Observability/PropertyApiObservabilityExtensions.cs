using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace PropertyApi.Observability;

public static class PropertyApiObservabilityExtensions
{
    private static readonly string[] AllowedOtlpProtocols =
    [
        "grpc",
        "http/protobuf"
    ];

    public static WebApplicationBuilder AddPropertyApiObservability(
        this WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<PropertyApiObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(PropertyApiObservabilityOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ServiceName),
                "Observability:ServiceName is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.CorrelationHeaderName),
                "Observability:CorrelationHeaderName is required.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.Otlp.Endpoint) ||
                    Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out _),
                "Observability:Otlp:Endpoint must be an absolute URI when configured.")
            .Validate(
                options => AllowedOtlpProtocols.Contains(
                    options.Otlp.Protocol,
                    StringComparer.OrdinalIgnoreCase),
                "Observability:Otlp:Protocol must be grpc or http/protobuf.")
            .ValidateOnStart();

        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.ParentId |
                ActivityTrackingOptions.Tags;
        });

        var observabilityOptions = builder.Configuration
            .GetSection(PropertyApiObservabilityOptions.SectionName)
            .Get<PropertyApiObservabilityOptions>() ?? new PropertyApiObservabilityOptions();

        if (observabilityOptions.JsonConsoleEnabled)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "O";
                options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
                {
                    Indented = false
                };
            });
        }
        else
        {
            builder.Services.Configure<SimpleConsoleFormatterOptions>(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "O";
            });
        }

        return builder;
    }

    public static IApplicationBuilder UsePropertyApiObservability(
        this IApplicationBuilder app)
    {
        app.ApplicationServices
            .GetRequiredService<IOptions<PropertyApiObservabilityOptions>>()
            .Value
            .GetType();

        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
