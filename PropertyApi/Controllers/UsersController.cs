using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Users.Commands.ChangePassword;
using PropertyApi.Application.Users.Commands.DeleteUser;
using PropertyApi.Application.Users.Commands.UpdateUser;
using PropertyApi.Application.Users.Queries.GetCurrentUser;
using PropertyApi.Application.Users.Queries.GetUserById;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
        => _sender = sender;

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var user = await _sender.Send(new GetCurrentUserQuery(userId), ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var user = await _sender.Send(new GetUserByIdQuery(id), ct);
        return user is null ? NotFound() : Ok(user);
    }

    // 🗑️ إزالة: كان هنا GET /api/users (Admin، بلا ترقيم صفحات) — أُزيل لأنه
    // كان مكرَّراً وظيفياً مع GET /api/admin/users (AdminController.GetUsers)
    // الذي يوفّر نفس البيانات مع ترقيم صفحات، وهو المُستخدَم فعلياً بالواجهة.
    // فحصتُ الاستخدام قبل الحذف: GetAllUsersQuery لم يكن مُستدعى من أي مكان
    // آخر بالباك اند (Controller وحيد فقط)، والواجهة لا تستهلكه إطلاقاً.
    // القرار وليس حذف عشوائي: تقليل السطح العام غير المُستخدَم لتسهيل الصيانة.

    [HttpPut("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new UpdateUserCommand(
            UserId: userId,
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new ChangePasswordCommand(
            UserId: userId,
            CurrentPassword: dto.CurrentPassword,
            NewPassword: dto.NewPassword,
            IpAddress: GetClientIp()), ct);

        if (result.NotFound)
            return Unauthorized();

        if (!result.Success)
            return BadRequest(new { errors = result.Errors });

        return NoContent();
    }

    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteAccount(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new DeleteUserCommand(userId), ct);

        if (result.NotFound)
            return Unauthorized();

        if (!result.Success)
            return BadRequest(new { errors = result.Errors });

        return NoContent();
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdText, out userId);
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);
