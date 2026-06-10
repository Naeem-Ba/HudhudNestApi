using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Users.Commands.UpdateUser;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

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

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || user.IsDeleted)
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            ProfileImageUrl = user.ProfileImageUrl,
            PreferredLanguage = user.PreferredLanguage,
            PreferredCurrency = user.PreferredCurrency,
            CountryCode = user.CountryCode,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            Roles = roles.ToList().AsReadOnly()
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null || user.IsDeleted)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        var isAgent = roles.Any(role => string.Equals(role, RoleNames.Agent, StringComparison.OrdinalIgnoreCase));

        return Ok(new UserSummaryDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
            ProfileImageUrl = user.ProfileImageUrl,
            PhoneNumber = isAgent ? user.PhoneNumber : null,
            Roles = roles.ToList().AsReadOnly()
        });
    }

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

    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(
            user,
            dto.CurrentPassword,
            dto.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _userManager.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Unauthorized();

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

public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
