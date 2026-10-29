using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Listings.Views;
using HudhudNestApi.Domain.Listings.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Listing view counter. A "view" is the detail page being opened, reported by the client once per
/// page open - never by the GET that fetches the listing (those are output-cached, called by search
/// cards' prefetching, crawlers and the owner's own edit screens, so they say nothing about a person
/// looking at the page). The client never sends a count; the server decides what counts.
/// </summary>
[ApiController]
public sealed partial class ListingViewsController : ControllerBase
{
    public const string VisitorHeader = "X-Visitor-Id";
    public const string TestTrafficHeader = "X-HN-Test-Traffic";

    private readonly ISender _mediator;
    private readonly IConfiguration _configuration;

    public ListingViewsController(ISender mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    // POST /api/properties/{id}/views
    [HttpPost("~/api/properties/{id:guid}/views")]
    [AllowAnonymous]
    [EnableRateLimiting("listing-views")]
    [ProducesResponseType(typeof(ListingViewResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> RecordPropertyView(Guid id, CancellationToken ct)
        => Record(ListingKind.Property, id, ct);

    // POST /api/short-stay/listings/{id}/views
    [HttpPost("~/api/short-stay/listings/{id:guid}/views")]
    [AllowAnonymous]
    [EnableRateLimiting("listing-views")]
    [ProducesResponseType(typeof(ListingViewResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> RecordShortStayView(Guid id, CancellationToken ct)
        => Record(ListingKind.ShortStay, id, ct);

    // GET /api/listing-views/stats?scope=mine|agency
    [HttpGet("~/api/listing-views/stats")]
    [Authorize]
    [ProducesResponseType(typeof(ListingViewStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStats([FromQuery] string? scope, CancellationToken ct)
    {
        if (GetUserId() is not { } userId)
            return Unauthorized();

        var result = await _mediator.Send(
            new GetListingViewStatsQuery(userId, User.IsInRole(RoleNames.AgencyOwner), scope ?? GetListingViewStatsQueryHandler.ScopeMine), ct);
        return Ok(result);
    }

    // GET /api/listing-views/{kind}/{id}/trend?days=30
    [HttpGet("~/api/listing-views/{kind}/{id:guid}/trend")]
    [Authorize]
    [ProducesResponseType(typeof(ListingViewTrendDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrend(string kind, Guid id, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (GetUserId() is not { } userId)
            return Unauthorized();

        if (!Enum.TryParse<ListingKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return BadRequest(new { message = "kind must be 'property' or 'shortstay'." });

        var result = await _mediator.Send(
            new GetListingViewTrendQuery(parsed, id, userId, User.IsInRole(RoleNames.Admin), User.IsInRole(RoleNames.AgencyOwner), days), ct);
        return Ok(result);
    }

    private async Task<IActionResult> Record(ListingKind kind, Guid id, CancellationToken ct)
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var visitor = Request.Headers[VisitorHeader].ToString();

        var result = await _mediator.Send(
            new RecordListingViewCommand(
                kind,
                id,
                GetUserId(),
                User.IsInRole(RoleNames.Admin),
                string.IsNullOrWhiteSpace(visitor) ? null : visitor.Trim(),
                Fingerprint(userAgent),
                IsAutomatedClient(userAgent),
                IsTestTraffic()),
            ct);

        // Never cacheable: the answer is per request.
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Coarse server-side fallback identity (network + user agent) for clients that sent no visitor id.</summary>
    private string Fingerprint(string userAgent)
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        var network = ip is null
            ? "unknown"
            : ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? string.Join('.', ip.ToString().Split('.').Take(3))   // /24
                : ip.ToString().Split(':').Take(4).Aggregate(string.Empty, (a, b) => a + b + ":"); // /64
        return $"{network}|{userAgent}";
    }

    /// <summary>
    /// Known crawlers, headless browsers, HTTP libraries and uptime probes. This is a courtesy filter
    /// against honest bots, not a defence: anyone can fake a browser user agent, which is what the
    /// per-viewer cooldown and the rate limit are for. An empty user agent counts as automated -
    /// every real browser and our Capacitor shells send one.
    /// </summary>
    internal static bool IsAutomatedClient(string? userAgent)
        => string.IsNullOrWhiteSpace(userAgent) || AutomatedClientPattern().IsMatch(userAgent);

    [GeneratedRegex(
        @"bot\b|crawl|spider|slurp|headless|lighthouse|pagespeed|facebookexternalhit|whatsapp|telegram|preview|monitor|uptime|curl/|wget/|python-requests|python-urllib|aiohttp|httpclient|okhttp/\d.*\(compatible|go-http-client|java/|libwww|postman|insomnia|playwright|puppeteer|selenium|cypress",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AutomatedClientPattern();

    /// <summary>
    /// E2E and smoke traffic announces itself with the shared secret in <c>ListingViews:TestTrafficSecret</c>.
    /// With no secret configured the header is ignored, so nobody can switch counting off by guessing a header name.
    /// </summary>
    private bool IsTestTraffic()
    {
        var secret = _configuration["ListingViews:TestTrafficSecret"];
        if (string.IsNullOrEmpty(secret))
            return false;

        var supplied = Request.Headers[TestTrafficHeader].ToString();
        if (string.IsNullOrEmpty(supplied))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }
}
