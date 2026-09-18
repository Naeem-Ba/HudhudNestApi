using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.AppUpdates.Commands.CreateAppRelease;
using PropertyApi.Application.AppUpdates.Commands.DeleteAppRelease;
using PropertyApi.Application.AppUpdates.Commands.DisableAppRelease;
using PropertyApi.Application.AppUpdates.Commands.EnableAppRelease;
using PropertyApi.Application.AppUpdates.Commands.UpdateAppRelease;
using PropertyApi.Application.AppUpdates.DTOs;
using PropertyApi.Application.AppUpdates.Queries.GetAdminAppReleases;
using PropertyApi.Application.AppUpdates.Queries.GetAppReleaseById;
using PropertyApi.Domain.AppUpdates.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>
/// Admin management of AppRelease rows — the backend half of App Update Management (Phase 1).
/// See docs/app-update-management.md.
/// </summary>
[ApiController]
[Route("api/admin/app-releases")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminAppReleasesController : ControllerBase
{
    private readonly ISender _sender;

    public AdminAppReleasesController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] AdminAppReleaseFilterDto filter, CancellationToken ct)
        => Ok(await _sender.Send(new GetAdminAppReleasesQuery(filter), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var release = await _sender.Send(new GetAppReleaseByIdQuery(id), ct);
        return release is null ? NotFound() : Ok(release);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAppReleaseRequest request, CancellationToken ct)
    {
        var id = await _sender.Send(
            new CreateAppReleaseCommand(
                request.Platform, request.Version, request.MinimumSupportedVersion, request.StoreUrl,
                request.ReleaseNotesAr, request.ReleaseNotesEn, request.ReleaseNotesDe, request.ReleaseDate,
                request.IsEnabled, GetUserId(), GetIpAddress()),
            ct);

        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAppReleaseRequest request, CancellationToken ct)
    {
        await _sender.Send(
            new UpdateAppReleaseCommand(
                id, request.Version, request.MinimumSupportedVersion, request.StoreUrl,
                request.ReleaseNotesAr, request.ReleaseNotesEn, request.ReleaseNotesDe, request.ReleaseDate,
                GetUserId(), GetIpAddress()),
            ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/enable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct)
    {
        await _sender.Send(new EnableAppReleaseCommand(id, GetUserId(), GetIpAddress()), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        await _sender.Send(new DisableAppReleaseCommand(id, GetUserId(), GetIpAddress()), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _sender.Send(new DeleteAppReleaseCommand(id, GetUserId(), GetIpAddress()), ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (Guid.TryParse(raw, out var userId))
            return userId;

        throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record CreateAppReleaseRequest(
    AppPlatform Platform,
    string Version,
    string MinimumSupportedVersion,
    string? StoreUrl,
    string? ReleaseNotesAr,
    string? ReleaseNotesEn,
    string? ReleaseNotesDe,
    DateTime ReleaseDate,
    bool IsEnabled);

public sealed record UpdateAppReleaseRequest(
    string Version,
    string MinimumSupportedVersion,
    string? StoreUrl,
    string? ReleaseNotesAr,
    string? ReleaseNotesEn,
    string? ReleaseNotesDe,
    DateTime ReleaseDate);
