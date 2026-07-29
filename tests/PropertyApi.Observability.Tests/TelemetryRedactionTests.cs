using System.Diagnostics;
using PropertyApi.Observability;
using Xunit;

namespace PropertyApi.Observability.Tests;

public sealed class TelemetryRedactionTests
{
    private static readonly string[] SentinelValues =
    [
        "TEST_PASSWORD_SHOULD_NOT_APPEAR",
        "TEST_OTP_SHOULD_NOT_APPEAR",
        "TEST_ACCESS_TOKEN_SHOULD_NOT_APPEAR",
        "TEST_REFRESH_TOKEN_SHOULD_NOT_APPEAR",
        "TEST_PHONE_SHOULD_NOT_APPEAR",
        "TEST_EMAIL_SHOULD_NOT_APPEAR",
        "TEST_CONNECTION_STRING_SHOULD_NOT_APPEAR"
    ];

    [Fact]
    public void Processor_removes_prohibited_and_sensitive_attributes()
    {
        using var activity = new Activity("redaction-test").Start();
        var keys = new[]
        {
            "auth.password", "auth.otp", "auth.access_token", "auth.refresh_token",
            "user.phone", "user.email", "db.connection_string"
        };

        for (var index = 0; index < keys.Length; index++)
        {
            activity.SetTag(keys[index], SentinelValues[index]);
        }

        activity.SetTag("operation", "login");
        new TelemetryRedactionProcessor().OnEnd(activity);

        var serialized = string.Join('|', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain(SentinelValues, serialized.Contains);
        Assert.Contains("operation=login", serialized);
    }

    [Fact]
    public void Cardinality_policy_prohibits_user_controlled_metric_dimensions()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(),
            "PropertyApi", "Observability", "PropertyApiTelemetry.cs"));
        var prohibited = new[]
        {
            "user_id", "property_id", "trace_id", "phone_number", "email",
            "raw_path", "query_string", "exception_message", "cache_key", "file_name"
        };

        foreach (var dimension in prohibited)
        {
            Assert.DoesNotContain($"new(\"{dimension}\"", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PropertyApi.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
