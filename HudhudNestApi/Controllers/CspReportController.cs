using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Receives the browser's Content-Security-Policy violation reports (<c>report-uri</c> of the web app's
/// Report-Only policy) so the policy can be tightened from real traffic and finally enforced (QA phase 17, P17-D05).
/// Anonymous by nature, so it keeps almost nothing: a few whitelisted fields, cut short, with query strings and
/// fragments removed, written to the log at Information level. Nothing is stored in the database.
/// </summary>
[ApiController]
[Route("api/security")]
public sealed class CspReportController : ControllerBase
{
    private const int MaxBodyBytes = 8 * 1024;
    private const int MaxFieldLength = 200;
    private readonly ILogger<CspReportController> _logger;

    public CspReportController(ILogger<CspReportController> logger) => _logger = logger;

    [HttpPost("csp-report")]
    [AllowAnonymous]
    [EnableRateLimiting("csp-report")]
    [RequestSizeLimit(MaxBodyBytes)]
    [Consumes("application/csp-report", "application/reports+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Report(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }

        foreach (var summary in Summarize(buffer.ToArray()))
        {
            _logger.LogInformation(
                "CSP violation ({Disposition}) {Directive} on {Document} blocked {Blocked} sample {Sample}.",
                summary.Disposition, summary.Directive, summary.Document, summary.Blocked, summary.Sample);
        }

        return NoContent();
    }

    internal readonly record struct CspSummary(string Disposition, string Directive, string Document, string Blocked, string Sample);

    /// <summary>Accepts the legacy <c>{"csp-report":{...}}</c> body and the Reporting API array; anything else gives nothing.</summary>
    internal static IReadOnlyList<CspSummary> Summarize(byte[] body)
    {
        var result = new List<CspSummary>();
        try
        {
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var legacy))
            {
                Add(result, legacy, "document-uri", "blocked-uri", "effective-directive", "disposition", "script-sample");
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray().Take(10))
                {
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("body", out var b))
                        Add(result, b, "documentURL", "blockedURL", "effectiveDirective", "disposition", "sample");
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: nothing to log.
        }

        return result;
    }

    private static void Add(List<CspSummary> into, JsonElement e, string doc, string blocked, string directive, string disposition, string sample)
    {
        if (e.ValueKind != JsonValueKind.Object) return;
        into.Add(new CspSummary(
            Clean(Read(e, disposition)), Clean(Read(e, directive)),
            Clean(StripQuery(Read(e, doc))), Clean(StripQuery(Read(e, blocked))), Clean(Read(e, sample))));
    }

    private static string Read(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string StripQuery(string url)
    {
        var cut = url.IndexOfAny(['?', '#']);
        return cut >= 0 ? url[..cut] : url;
    }

    /// <summary>Single line, no control characters (a report is attacker-supplied text; it must not forge log lines), cut short.</summary>
    internal static string Clean(string value)
    {
        var chars = value.Where(c => !char.IsControl(c)).Take(MaxFieldLength).ToArray();
        return new string(chars);
    }
}
