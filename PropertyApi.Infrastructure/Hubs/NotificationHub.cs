using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PropertyApi.Application.Notifications.Interfaces;
using Microsoft.Extensions.Logging;

namespace PropertyApi.Infrastructure.Hubs;

/// <summary>
/// SignalR hub for authenticated real-time notifications.
/// Each connection joins group: user_{userId}.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    private readonly INotificationService _notifications;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        INotificationService notifications,
        ILogger<NotificationHub> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserIdText();

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning(
                "SignalR notification connection rejected because the user id claim is missing. ConnectionId={ConnectionId}",
                Context.ConnectionId);

            Context.Abort();
            return;
        }

        var groupName = GetGroupName(userId);

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            groupName,
            Context.ConnectionAborted);

        _logger.LogInformation(
            "SignalR notification connection established. UserId={UserId}, Group={Group}, ConnectionId={ConnectionId}",
            userId,
            groupName,
            Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetCurrentUserIdText();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                GetGroupName(userId),
                CancellationToken.None);
        }

        if (exception is not null)
        {
            _logger.LogWarning(
                exception,
                "SignalR notification connection closed with an error. ConnectionId={ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client may call: connection.invoke("MarkNotificationRead", notificationId)
    /// This updates the database and returns the updated unread count to the caller.
    /// </summary>
    public async Task MarkNotificationRead(Guid notificationId)
    {
        var userId = GetCurrentUserId();

        if (userId is null)
            throw new HubException("Authentication is required.");

        await _notifications.MarkAsReadAsync(
            notificationId,
            userId.Value,
            Context.ConnectionAborted);

        var unreadCount = await _notifications.GetUnreadCountAsync(
            userId.Value,
            Context.ConnectionAborted);

        await Clients.Caller.SendAsync(
            "UnreadCountChanged",
            unreadCount,
            Context.ConnectionAborted);
    }

    public static string GetGroupName(string userId) => $"user_{userId}";

    private string? GetCurrentUserIdText()
    {
        return Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private Guid? GetCurrentUserId()
    {
        var userIdText = GetCurrentUserIdText();

        return Guid.TryParse(userIdText, out var userId)
            ? userId
            : null;
    }
}
