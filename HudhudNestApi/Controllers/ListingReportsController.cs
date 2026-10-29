using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Moderation;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Controllers;

/// <summary>
/// The reporter's side of listing reports. Signed-in only (no anonymous reports: the existing
/// account system is the identity, and an account is what makes abuse traceable and limitable).
/// The reporter's id always comes from the token; every lookup is scoped to it, so another
/// person's report id answers 404. The listing owner has no endpoint here and never sees reports.
/// Admin review lives in <see cref="AdminListingReportsController"/>.
/// </summary>
[ApiController]
[Route("api/listing-reports")]
[Authorize]
public sealed class ListingReportsController : ControllerBase
{
    private readonly ISender _mediator;

    public ListingReportsController(ISender mediator) => _mediator = mediator;

    /// <summary>The reasons a listing can be reported for, as stable codes the client translates.</summary>
    [HttpGet("reasons")]
    [ProducesResponseType(typeof(IReadOnlyList<ListingReportReasonDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reasons(CancellationToken ct)
        => Ok(await _mediator.Send(new GetListingReportReasonsQuery(), ct));

    [HttpPost]
    [EnableRateLimiting("listing-reports")]
    [ProducesResponseType(typeof(MyListingReportDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MyListingReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit([FromBody] SubmitListingReportRequest body, CancellationToken ct)
    {
        var result = await _mediator.Send(new SubmitListingReportCommand(
            UserId(), body.PropertyId, body.Reason, body.Details, body.ClientRequestId), ct);

        // A replay of the same dialog's request answers 200 with the report it already created.
        return result.Created
            ? CreatedAtAction(nameof(GetMine), new { id = result.Report.Id }, result.Report)
            : Ok(result.Report);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(ListingReportPageDto<MyListingReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _mediator.Send(new GetMyListingReportsQuery(UserId(), page, pageSize), ct));

    /// <summary>Whether the caller already has an open report on this listing.</summary>
    [HttpGet("me/open")]
    [ProducesResponseType(typeof(MyOpenReportStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenForListing([FromQuery] Guid propertyId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetMyOpenReportForListingQuery(UserId(), propertyId), ct));

    [HttpGet("me/{id:guid}")]
    [ProducesResponseType(typeof(MyListingReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMine(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetMyListingReportQuery(UserId(), id), ct));

    /// <summary>Answer an admin's question. Adds to the report; the original report is never edited.</summary>
    [HttpPost("me/{id:guid}/respond")]
    [EnableRateLimiting("listing-reports")]
    [ProducesResponseType(typeof(MyListingReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondToListingReportRequest body, CancellationToken ct)
        => Ok(await _mediator.Send(new RespondToListingReportCommand(UserId(), id, body.Message), ct));

    private Guid UserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id)
            ? id
            : throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }

    /// <summary>Only what the reporter decides. No status, priority, owner or reporter id: those are the server's.</summary>
    public sealed record SubmitListingReportRequest(
        Guid PropertyId,
        ListingReportReason Reason,
        string? Details,
        Guid? ClientRequestId);

    public sealed record RespondToListingReportRequest(string Message);
}
