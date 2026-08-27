using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Listings.Commands.DeletePropertyImage;
using PropertyApi.Application.Listings.Commands.SetMainPropertyImage;
using PropertyApi.Application.Listings.Commands.UploadPropertyImages;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Queries.GetPropertyImages;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/properties/{propertyId:guid}/images")]
[Authorize]
public sealed class PropertyImagesController : ControllerBase
{
    private readonly ISender _sender;
    private const int MaxFilesPerUpload = 10;

    public PropertyImagesController(ISender sender)
        => _sender = sender;

    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(
        Guid propertyId,
        [FromForm] IFormFileCollection files,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { message = "No files uploaded." });

        if (files.Count > MaxFilesPerUpload)
            return BadRequest(new { message = $"Upload at most {MaxFilesPerUpload} files per request." });

        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var uploadFiles = files
            .Select(file => new UploadPropertyImageFileDto(
                file.OpenReadStream(),
                file.FileName,
                file.ContentType,
                file.Length))
            .ToList();

        var result = await _sender.Send(
            new UploadPropertyImagesCommand(propertyId, userId.Value, uploadFiles),
            ct);

        return result.Status switch
        {
            PropertyImageMutationStatus.Success => Ok(result.Images),
            PropertyImageMutationStatus.NotFound => NotFound(),
            PropertyImageMutationStatus.Forbidden => Forbid(),
            PropertyImageMutationStatus.StorageFailed => BadRequest(new { message = result.Message }),
            PropertyImageMutationStatus.ValidationFailed => BadRequest(new { message = result.Message }),
            _ => BadRequest(new { message = result.Message })
        };
    }

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAll(
        Guid propertyId,
        CancellationToken ct)
    {
        var result = await _sender.Send(new GetPropertyImagesQuery(propertyId), ct);

        return result.Found
            ? Ok(result.Images)
            : NotFound();
    }

    [HttpPatch("{imageId:guid}/setmain")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMain(
        Guid propertyId,
        Guid imageId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await _sender.Send(
            new SetMainPropertyImageCommand(propertyId, imageId, userId.Value),
            ct);

        return ToMutationActionResult(result);
    }

    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid propertyId,
        Guid imageId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await _sender.Send(
            new DeletePropertyImageCommand(propertyId, imageId, userId.Value),
            ct);

        return ToMutationActionResult(result);
    }

    private IActionResult ToMutationActionResult(PropertyImageMutationResult result)
    {
        return result.Status switch
        {
            PropertyImageMutationStatus.Success => NoContent(),
            PropertyImageMutationStatus.NotFound => NotFound(),
            PropertyImageMutationStatus.Forbidden => Forbid(),
            PropertyImageMutationStatus.ValidationFailed => BadRequest(new { message = result.Message }),
            PropertyImageMutationStatus.StorageFailed => BadRequest(new { message = result.Message }),
            _ => BadRequest(new { message = result.Message })
        };
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
