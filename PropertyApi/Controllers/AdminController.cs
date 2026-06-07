using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Controllers;

/// <summary>
/// لوحة تحكم المدير.
/// يُضيف:
///   - قائمة المستخدمين
///   - تفعيل/تعطيل المستخدمين (Soft Delete)
///   ///          يجب تحديث الـ Frontend
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly AppDbContext _db;

    public AdminController(
    UserManager<User> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    AppDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    // ------------------------------------------------------------------
    // GET /api/admin/users  (قائمة كل المستخدمين)
    // ------------------------------------------------------------------
    [HttpGet("users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        // ✅ التحقق أولاً قبل أي استعلام
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _userManager.Users
            .Where(u => !u.IsDeleted)
            .OrderByDescending(u => u.CreatedAt);

        var total = await query.CountAsync();
        var users = await query
            .Skip((page - 1) * pageSize) 
            .Take(pageSize)
            .ToListAsync();

        var result = new List<object>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.DisplayName,
                u.IsAgent,
                u.CreatedAt,
                Roles = roles
            });
        }

        return Ok(new { total, page, pageSize, data = result });
    }

    // ------------------------------------------------------------------
    // PUT /api/admin/users/{id}/role  (تغيير دور المستخدم)
    // مُرحَّل من: Legacy api/benutzer/{id}/role
    // ------------------------------------------------------------------
    [HttpPut("users/{id:guid}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(
    Guid id,
    [FromBody] UpdateRoleRequest request,
    CancellationToken ct)
    {
        var requestedRole = RoleNames.All.FirstOrDefault(
            role => string.Equals(
                role,
                request.Role,
                StringComparison.OrdinalIgnoreCase));

        if (requestedRole is null)
        {
            return BadRequest(new
            {
                message = "The requested role is invalid.",
                allowedRoles = RoleNames.All
            });
        }

        var user = await _userManager.FindByIdAsync(id.ToString());

        if (user is null || user.IsDeleted)
            return NotFound(new { message = "User not found." });

        await using var transaction =
            await _db.Database.BeginTransactionAsync(ct);

        var currentRoles = await _userManager.GetRolesAsync(user);

        var removeResult = await _userManager.RemoveFromRolesAsync(
            user,
            currentRoles);

        if (!removeResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);

            return BadRequest(new
            {
                message = "Could not remove the old roles.",
                errors = removeResult.Errors.Select(
                    error => error.Description)
            });
        }

        var addResult = await _userManager.AddToRoleAsync(
            user,
            requestedRole);

        if (!addResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);

            return BadRequest(new
            {
                message = "Could not add the new role.",
                errors = addResult.Errors.Select(
                    error => error.Description)
            });
        }

        user.IsAgent = requestedRole == RoleNames.Agent;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);

            return BadRequest(new
            {
                message = "Could not update the user.",
                errors = updateResult.Errors.Select(
                    error => error.Description)
            });
        }

        await transaction.CommitAsync(ct);

        return Ok(new
        {
            message = $"Role updated to '{requestedRole}'."
        });
    }


    // ------------------------------------------------------------------
    // DELETE /api/admin/users/{id}  (تعطيل مستخدم - Soft Delete)
    // ------------------------------------------------------------------
    [HttpDelete("users/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableUser(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _userManager.UpdateAsync(user);


        return NoContent();
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────
public sealed record UpdateRoleRequest(string Role);