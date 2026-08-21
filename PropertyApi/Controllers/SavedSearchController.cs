using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Search.Commands.CreateSavedSearch;
using PropertyApi.Application.Search.Commands.DeleteSavedSearch;
using PropertyApi.Application.Search.Queries.GetMySavedSearches;

namespace PropertyApi.Controllers;

/// <summary>Phase-0, Task 3. Mirrors FavoritesController's shape.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class SavedSearchController : ControllerBase
{
    private readonly ISender _sender;

    public SavedSearchController(ISender sender)
        => _sender = sender;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await _sender.Send(new GetMySavedSearchesQuery(userId.Value), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSavedSearchCommand command,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var cmd = command with { UserId = userId.Value };
        var id = await _sender.Send(cmd, ct);

        return StatusCode(StatusCodes.Status201Created, new { id });
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var success = await _sender.Send(new DeleteSavedSearchCommand(id, userId.Value), ct);
        return success ? NoContent() : NotFound();
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
