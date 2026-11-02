using System.Security.Claims;
using HudhudNestApi.Application.Demand.Commands;
using HudhudNestApi.Application.Demand.DTOs;
using HudhudNestApi.Application.Demand.Queries;
using HudhudNestApi.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Growth engine P2: property search requests ("I am looking for…"). Every endpoint needs a signed-in user;
/// a request is only ever returned in full to its own author, advertisers see the anonymous form.
/// </summary>
[ApiController]
[Route("api/search-requests")]
[Authorize]
public sealed class SearchRequestsController : ControllerBase
{
    private readonly ISender _sender;

    public SearchRequestsController(ISender sender) => _sender = sender;

    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<SearchRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await _sender.Send(new GetMySearchRequestsQuery(UserId()), ct));

    [HttpPost]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(typeof(SearchRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSearchRequestCommand command, CancellationToken ct)
    {
        var result = await _sender.Send(command with { UserId = UserId() }, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(typeof(SearchRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSearchRequestCommand command, CancellationToken ct)
        => Ok(await _sender.Send(command with { Id = id, UserId = UserId() }, ct));

    [HttpPost("{id:guid}/pause")]
    [EnableRateLimiting("user-write")]
    public Task<IActionResult> Pause(Guid id, CancellationToken ct) => Change(id, SearchRequestAction.Pause, null, ct);

    [HttpPost("{id:guid}/resume")]
    [EnableRateLimiting("user-write")]
    public Task<IActionResult> Resume(Guid id, CancellationToken ct) => Change(id, SearchRequestAction.Resume, null, ct);

    [HttpPost("{id:guid}/close")]
    [EnableRateLimiting("user-write")]
    public Task<IActionResult> Close(Guid id, CancellationToken ct) => Change(id, SearchRequestAction.Close, null, ct);

    public sealed record RenewBody(DateTime? ExpiresAt);

    [HttpPost("{id:guid}/renew")]
    [EnableRateLimiting("user-write")]
    public Task<IActionResult> Renew(Guid id, [FromBody] RenewBody? body, CancellationToken ct)
        => Change(id, SearchRequestAction.Renew, body?.ExpiresAt, ct);

    [HttpGet("{id:guid}/matches")]
    [ProducesResponseType(typeof(IReadOnlyList<SearchRequestMatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatches(Guid id, CancellationToken ct)
        => Ok(await _sender.Send(new GetSearchRequestMatchesQuery(id, UserId()), ct));

    /// <summary>Anonymous active requests, for users with at least one listing on the market (403 otherwise).</summary>
    [HttpGet("board")]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(typeof(PagedSearchRequests), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBoard(
        [FromQuery] ListingType? listingType, [FromQuery] int? governorateId, [FromQuery] int? propertyTypeId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _sender.Send(new GetSearchRequestBoardQuery(UserId(), listingType, governorateId, propertyTypeId, page, pageSize), ct));

    /// <summary>Requests that fit one of the caller's own listings (404/403 for someone else's listing).</summary>
    [HttpGet("for-listing/{propertyId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<ListingDemandDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForListing(Guid propertyId, CancellationToken ct)
        => Ok(await _sender.Send(new GetListingDemandQuery(propertyId, UserId()), ct));

    public sealed record ProposeBody(Guid PropertyId);

    /// <summary>Suggest one of the caller's listings for a request. The requester is notified; nothing about them is returned.</summary>
    [HttpPost("{id:guid}/proposals")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Propose(Guid id, [FromBody] ProposeBody body, CancellationToken ct)
    {
        await _sender.Send(new ProposeListingCommand(id, body.PropertyId) { UserId = UserId() }, ct);
        return NoContent();
    }

    private async Task<IActionResult> Change(Guid id, SearchRequestAction action, DateTime? expiresAt, CancellationToken ct)
        => Ok(await _sender.Send(new ChangeSearchRequestStatusCommand(id, UserId(), action, expiresAt), ct));

    private Guid UserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : throw new UnauthorizedAccessException();
    }
}
