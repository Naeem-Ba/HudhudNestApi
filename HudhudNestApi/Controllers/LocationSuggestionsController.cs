using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Admin moderation queue for user-typed district/neighborhood names that
/// aren't in the seeded catalog — see LocationSuggestion's doc comment.
/// Everything here is Admin-only; regular users never see this list, they
/// just type a name on property-form and it lands here automatically.
/// </summary>
[ApiController]
[Route("api/admin/location-suggestions")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class LocationSuggestionsController : ControllerBase
{
    private readonly ILocationSuggestionService _suggestions;

    public LocationSuggestionsController(ILocationSuggestionService suggestions)
    {
        _suggestions = suggestions;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPending(CancellationToken ct)
        => Ok(await _suggestions.GetPendingAsync(ct));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ReviewLocationSuggestionRequest? request, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        try
        {
            var resultingId = await _suggestions.ApproveAsync(id, actorId.Value, request?.Notes, ct);
            return Ok(new { resultingEntityId = resultingId });
        }
        catch (DomainException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ReviewLocationSuggestionRequest? request, CancellationToken ct)
    {
        var actorId = GetCurrentUserId();
        if (actorId is null)
            return Unauthorized();

        try
        {
            await _suggestions.RejectAsync(id, actorId.Value, request?.Notes, ct);
            return NoContent();
        }
        catch (DomainException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

public sealed record ReviewLocationSuggestionRequest(string? Notes);
