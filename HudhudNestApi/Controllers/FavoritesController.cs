using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Favorites.Commands.AddFavorite;
using HudhudNestApi.Application.Favorites.Commands.RemoveFavorite;
using HudhudNestApi.Application.Favorites.DTOs;
using HudhudNestApi.Application.Favorites.Queries.GetMyFavorites;

namespace HudhudNestApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class FavoritesController : ControllerBase
{
    private readonly ISender _sender;

    public FavoritesController(ISender sender)
        => _sender = sender;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyFavorites(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var favorites = await _sender.Send(new GetMyFavoritesQuery(userId.Value), ct);
        return Ok(favorites);
    }

    [HttpPost("{propertyId:guid}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add(Guid propertyId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await _sender.Send(new AddFavoriteCommand(userId.Value, propertyId), ct);

        return result.Status switch
        {
            FavoriteMutationStatus.Success => StatusCode(StatusCodes.Status201Created, new { message = result.Message }),
            FavoriteMutationStatus.NotFound => NotFound(new { message = result.Message }),
            FavoriteMutationStatus.Conflict => Conflict(new { message = result.Message }),
            _ => BadRequest(new { message = result.Message })
        };
    }

    [HttpDelete("{propertyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid propertyId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await _sender.Send(new RemoveFavoriteCommand(userId.Value, propertyId), ct);

        return result.Status switch
        {
            FavoriteMutationStatus.Success => NoContent(),
            FavoriteMutationStatus.NotFound => NotFound(),
            _ => BadRequest(new { message = result.Message })
        };
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
