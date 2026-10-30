using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Listings.Queries.GetListingTranslation;
using HudhudNestApi.Domain.Listings.Translations;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Automatic translation of a listing's title and description. Opening a listing calls this once per
/// language; the answer is stored permanently, so later calls (any user, any device) are a database read.
/// Deliberately not output-cached: it must reflect an edit or deletion immediately, and the stored row
/// is the cache. Never needed for the listing itself to load - the detail endpoint is unchanged.
/// </summary>
[ApiController]
public sealed class ListingTranslationsController : ControllerBase
{
    private readonly ISender _mediator;

    public ListingTranslationsController(ISender mediator) => _mediator = mediator;

    // POST (not GET): a first call may create the translation and spend provider quota, so it must
    // not be triggered by prefetchers or link previews.
    [HttpPost("api/properties/{id:guid}/translation")]
    [AllowAnonymous]
    [EnableRateLimiting("listing-translation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Ensure(Guid id, [FromQuery] string? lang, CancellationToken ct)
        => Run(ListingRef.ForProperty(id), lang, ct);

    [HttpPost("api/short-stay/listings/{id:guid}/translation")]
    [AllowAnonymous]
    [EnableRateLimiting("listing-translation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> EnsureShortStay(Guid id, [FromQuery] string? lang, CancellationToken ct)
        => Run(ListingRef.ForShortStay(id), lang, ct);

    private async Task<IActionResult> Run(ListingRef listing, string? lang, CancellationToken ct)
    {
        var language = SupportedPreferences.NormalizeLanguage(lang);
        if (language is null)
            return BadRequest(new { message = "lang must be one of: ar, de, en." });

        var result = await _mediator.Send(new GetListingTranslationQuery(listing, language), ct);
        if (result is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }
}
