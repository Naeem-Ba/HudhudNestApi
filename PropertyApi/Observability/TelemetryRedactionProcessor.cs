using System.Diagnostics;
using OpenTelemetry;

namespace PropertyApi.Observability;

public sealed class TelemetryRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> ProhibitedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http.request.header.authorization",
        "http.request.header.cookie",
        "http.response.header.set-cookie",
        "db.connection_string",
        "db.statement",
        "db.query.text",
        "url.query",
        "url.full",
        "user.email",
        "user.phone",
        "auth.token",
        "auth.otp",
        "redis.key",
        "server.address"
    };

    public override void OnEnd(Activity activity)
    {
        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (ProhibitedAttributes.Contains(tag.Key) || LooksSensitive(tag.Key))
            {
                activity.SetTag(tag.Key, null);
            }
        }
    }

    private static bool LooksSensitive(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("connection_string", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("query", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("email", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("otp", StringComparison.OrdinalIgnoreCase);
    }
}
