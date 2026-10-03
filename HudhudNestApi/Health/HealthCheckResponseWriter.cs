using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HudhudNestApi.Health;

internal static class HealthCheckResponseWriter
{
    public static async Task WriteMinimalAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        // Status only. The endpoints are anonymous, and per-dependency names and timings
        // fingerprint the backing stack for nobody's benefit (security audit 2026-10-03, F-08);
        // the HTTP status code already carries what orchestrators and gates act on.
        var response = new
        {
            status = report.Status.ToString()
        };

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
