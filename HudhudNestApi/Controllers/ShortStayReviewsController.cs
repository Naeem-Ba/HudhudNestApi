using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.ShortStay.Commands.AddShortStayReview;
using HudhudNestApi.Application.ShortStay.DTOs;

namespace HudhudNestApi.Controllers;

[ApiController]
[Route("api/short-stay/reviews")]
[Authorize]
public sealed class ShortStayReviewsController : ControllerBase
{
    private readonly ISender _mediator;
    public ShortStayReviewsController(ISender mediator) => _mediator = mediator;

    [HttpPost]
    [EnableRateLimiting("reviews")]
    [ProducesResponseType(typeof(ShortStayReviewDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Add([FromBody] AddShortStayReviewRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AddShortStayReviewCommand(dto.BookingId, GetUserId(), dto.Rating, dto.Comment), ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (Guid.TryParse(raw, out var userId))
            return userId;

        throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }
}

public sealed record AddShortStayReviewRequest(Guid BookingId, int Rating, string? Comment);
