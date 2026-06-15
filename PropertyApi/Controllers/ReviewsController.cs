using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Reviews.Commands.AddReview;
using PropertyApi.Application.Reviews.Commands.DeleteReview;
using PropertyApi.Application.Reviews.DTOs;
using PropertyApi.Application.Reviews.Queries.GetPropertyReviews;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ReviewsController : ControllerBase
{
    private readonly ISender _mediator;
    public ReviewsController(ISender mediator) => _mediator = mediator;

    // ── GET /api/reviews/property/{propertyId} ────────────────────
    [HttpGet("property/{propertyId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PropertyReviewSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyReviews(
        Guid propertyId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetPropertyReviewsQuery(propertyId, page, pageSize), ct);
        return Ok(result);
    }

    // ── POST /api/reviews ─────────────────────────────────────────
    [HttpPost]
    [Authorize]
    [EnableRateLimiting("reviews")]
    [ProducesResponseType(typeof(PropertyReviewDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Add(
        [FromBody] AddReviewRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AddReviewCommand(
                dto.PropertyId,
                GetUserId(),
                dto.Rating,
                dto.Comment), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // ── DELETE /api/reviews/{id} ──────────────────────────────────
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var isAdmin = User.IsInRole(RoleNames.Admin);
        await _mediator.Send(
            new DeleteReviewCommand(id, GetUserId(), isAdmin), ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(raw);
    }
}

public sealed record AddReviewRequest(
    Guid PropertyId,
    int Rating,
    string? Comment
);