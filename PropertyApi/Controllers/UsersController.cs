using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Entities;
using MediatR;
using PropertyApi.Application.Users.Commands.UpdateUser;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly UserManager<User> _userManager;
private readonly ISender _mediator;

public UsersController(UserManager<User> userManager, ISender mediator)
{
    _userManager = userManager;
    _mediator = mediator;
}

    // -- GET /api/users/me -------------------------------------
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || user.IsDeleted) return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            IsAgent = user.IsAgent,
            ProfileImageUrl = user.ProfileImageUrl,
            PreferredLanguage = user.PreferredLanguage,
            PreferredCurrency = user.PreferredCurrency,
            CountryCode = user.CountryCode,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            Roles = roles.ToList().AsReadOnly()
        });
    }

    // -- GET /api/users/{id} -----------------------------------
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null || user.IsDeleted) return NotFound();

        // Public profile — no sensitive fields
        return Ok(new UserSummaryDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
            ProfileImageUrl = user.ProfileImageUrl,
            IsAgent = user.IsAgent,
            PhoneNumber = user.IsAgent ? user.PhoneNumber : null // expose phone only for agents
        });
    }

    // -- PUT /api/users/me -------------------------------------
    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(
    [FromBody] UpdateProfileRequest dto,
    CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id))
            return Unauthorized();

        var result = await _mediator.Send(new UpdateUserCommand(
            UserId: id,
            FirstName: dto.FirstName,
            LastName: dto.LastName,
            DisplayName: dto.DisplayName,
            PhoneNumber: dto.PhoneNumber,
            ProfileImageUrl: null,
            PreferredLanguage: null,
            PreferredCurrency: null,
            CountryCode: null
        ), ct);

        return result is null ? NotFound() : NoContent();
    }

    // -- POST /api/users/me/change-password --------------------
    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(
            user, dto.CurrentPassword, dto.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        //SecurityStamp يُبطل جميع tokens الحالية فوراً
        await _userManager.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    // -- DELETE /api/users/me ----------------------------------
    // Soft delete — does NOT hard-delete the user
    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                errors = result.Errors.Select(error => error.Description)
            });
        }

        return NoContent();
    }
}

// -- Request DTOs ---------------------------------------------
public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
