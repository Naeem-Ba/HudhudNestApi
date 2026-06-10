using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Application.Notifications.Interfaces;

namespace PropertyApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(
        INotificationService notifications,
        ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Unauthorized();

        var result = await _notifications.GetUserNotificationsAsync(
            userId.Value,
            page,
            pageSize,
            ct);

        return Ok(result);
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Unauthorized();

        var count = await _notifications.GetUnreadCountAsync(
            userId.Value,
            ct);

        return Ok(new { UnreadCount = count });
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAsRead(
        Guid id,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Unauthorized();

        await _notifications.MarkAsReadAsync(
            id,
            userId.Value,
            ct);

        return NoContent();
    }

    [HttpPatch("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        if (userId is null)
            return Unauthorized();

        await _notifications.MarkAllAsReadAsync(
            userId.Value,
            ct);

        return NoContent();
    }
}
