namespace HudhudNestApi.Architecture.Tests;

public sealed class ObservabilityGuardTests
{
    [Fact(DisplayName = "Observability must expose correlation trace metrics and redaction-safe request groups")]
    public void ApiObservability_Should_Define_Correlation_Trace_Metrics_And_Redaction_Guards()
    {
        var repoRoot = FindRepositoryRoot();
        var middleware = ReadSource(repoRoot, "HudhudNestApi", "Observability", "CorrelationIdMiddleware.cs");
        var telemetry = ReadSource(repoRoot, "HudhudNestApi", "Observability", "HudhudNestApiTelemetry.cs");
        var classifier = ReadSource(repoRoot, "HudhudNestApi", "Observability", "RequestTelemetryClassifier.cs");
        var extensions = ReadSource(repoRoot, "HudhudNestApi", "Observability", "HudhudNestApiObservabilityExtensions.cs");

        Assert.Contains("X-Correlation-ID", ReadSource(repoRoot, "HudhudNestApi", "Observability", "HudhudNestApiObservabilityOptions.cs"));
        Assert.Contains("TraceId", middleware);
        Assert.Contains("SpanId", middleware);
        Assert.Contains("CorrelationId", middleware);
        Assert.Contains("ActivitySource", telemetry);
        Assert.Contains("Meter.CreateCounter", telemetry);
        Assert.Contains("Meter.CreateHistogram", telemetry);
        Assert.Contains("hudhudnest.auth.requests", telemetry);
        Assert.Contains("hudhudnest.properties.requests", telemetry);
        Assert.Contains("route_group", telemetry);
        Assert.Contains("operation", telemetry);
        Assert.Contains("ActivityTrackingOptions.TraceId", extensions);
        Assert.Contains("ActivityTrackingOptions.SpanId", extensions);

        Assert.DoesNotContain("Request.QueryString", middleware);
        Assert.DoesNotContain("Request.Body", middleware);
        Assert.DoesNotContain("Authorization", middleware);
        Assert.DoesNotContain("RefreshToken", middleware);
        Assert.DoesNotContain("PhoneNumber", middleware);
        Assert.DoesNotContain("Email", middleware);
        Assert.DoesNotContain("userId", telemetry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IpAddress", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.TryParse", classifier);
    }

    [Fact(DisplayName = "Application layer must create telemetry spans around MediatR requests")]
    public void ApplicationObservability_Should_Trace_MediatR_Requests()
    {
        var repoRoot = FindRepositoryRoot();
        var dependencyInjection = ReadSource(repoRoot, "HudhudNestApi.Application", "DependencyInjection.cs");
        var behavior = ReadSource(repoRoot, "HudhudNestApi.Application", "Common", "Behaviors", "TelemetryBehavior.cs");
        var telemetry = ReadSource(repoRoot, "HudhudNestApi.Application", "Common", "Observability", "ApplicationTelemetry.cs");

        Assert.Contains("TelemetryBehavior<,>", dependencyInjection);
        Assert.Contains("ApplicationTelemetry.ActivitySource.StartActivity", behavior);
        Assert.Contains("application.request.kind", behavior);
        Assert.Contains("ActivityStatusCode.Error", behavior);
        Assert.Contains("hudhudnest.application.requests", telemetry);
        Assert.Contains("hudhudnest.application.request.duration", telemetry);
        Assert.DoesNotContain("Email", behavior);
        Assert.DoesNotContain("PhoneNumber", behavior);
        Assert.DoesNotContain("AccessToken", behavior);
        Assert.DoesNotContain("RefreshToken", behavior);
        Assert.DoesNotContain("IdToken", behavior);
    }

    private static string ReadSource(string repoRoot, params string[] relativePath)
        => File.ReadAllText(Path.Combine(new[] { repoRoot }.Concat(relativePath).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing HudhudNestApi.sln.");
    }
}
