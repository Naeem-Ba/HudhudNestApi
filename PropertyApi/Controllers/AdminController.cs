using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin)
    {
        _admin = admin;
    }

    // GET /api/admin/users?page=1&pageSize=20&role=User
    [HttpGet("users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? role = null,
        CancellationToken ct = default)
    {
        var result = await _admin.GetUsersWithPaginationAsync(
            page,
            pageSize,
            role,
            ct);

        return Ok(new
        {
            total = result.TotalCount,
            result.Page,
            result.PageSize,
            data = result.Items
        });
    }

    // GET /api/admin/roles
    [HttpGet("roles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetRoles()
        => Ok(_admin.GetRoles());

    // PUT /api/admin/users/{id}/role
    [HttpPut("users/{id:guid}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(
        Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken ct)
    {
        var result = await _admin.SetUserRoleAsync(id, request.Role, ct);
        return ToActionResult(result);
    }

    // POST /api/admin/users/{id}/roles/{role}
    [HttpPost("users/{id:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        Guid id,
        string role,
        CancellationToken ct)
    {
        var result = await _admin.AssignRoleAsync(id, role, ct);
        return ToActionResult(result);
    }

    // DELETE /api/admin/users/{id}/roles/{role}
    [HttpDelete("users/{id:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRole(
        Guid id,
        string role,
        CancellationToken ct)
    {
        var result = await _admin.RemoveRoleAsync(id, role, ct);
        return ToActionResult(result);
    }

    // DELETE /api/admin/users/{id}
    [HttpDelete("users/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableUser(
        Guid id,
        CancellationToken ct)
    {
        var result = await _admin.DisableUserAsync(id, ct);

        if (result.NotFound)
            return NotFound(new { message = result.Message });

        if (!result.Succeeded)
            return BadRequest(new { message = result.Message, errors = result.Errors });

        return NoContent();
    }

    private IActionResult ToActionResult(AdminOperationResult result)
    {
        if (result.Succeeded)
            return Ok(new { message = result.Message });

        if (result.NotFound)
            return NotFound(new { message = result.Message });

        if (result.Message == "The requested role is invalid.")
        {
            return BadRequest(new
            {
                message = result.Message,
                allowedRoles = result.Errors
            });
        }

        return BadRequest(new
        {
            message = result.Message,
            errors = result.Errors
        });
    }
}

public sealed record UpdateRoleRequest(string Role);
