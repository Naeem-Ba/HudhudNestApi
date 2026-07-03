using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Notifications.DTOs;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Notifications.Entities;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Infrastructure.Hubs;
using PropertyApi.Infrastructure.Notifications;

namespace PropertyApi.Integration.Tests.Notifications;

public sealed class NotificationServiceSignalRTests
{
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Notifications")]
    public async Task NotifyNewMessageAsync_PersistsNotification_AndPushesReceiveNotificationToRecipientGroup()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();

        var repository = new CapturingNotificationRepository();
        var hubContext = new CapturingNotificationHubContext();

        var service = new NotificationService(
            repository,
            hubContext,
            NullLogger<NotificationService>.Instance);

        // Act
        await service.NotifyNewMessageAsync(
            recipientId,
            senderId,
            "Test Sender",
            messageId,
            propertyId,
            CancellationToken.None);

        // Assert: persisted notification.
        var stored = Assert.Single(repository.Notifications);

        Assert.Equal(recipientId, stored.RecipientId);
        Assert.Equal(NotificationType.NewMessage, stored.Type);
        Assert.Equal(propertyId, stored.PropertyId);
        Assert.Equal(messageId, stored.RelatedEntityId);
        Assert.False(stored.IsRead);
        Assert.Contains("Test Sender", stored.Message, StringComparison.OrdinalIgnoreCase);

        // Assert: SignalR push contract.
        Assert.Equal(NotificationHub.GetGroupName(recipientId), hubContext.Clients.LastGroupName);
        Assert.Equal(NotificationHub.ReceiveNotificationEvent, hubContext.Clients.LastMethodName);

        var payload = Assert.Single(hubContext.Clients.LastArguments);
        var dto = Assert.IsType<NotificationDto>(payload);

        Assert.Equal(stored.Id, dto.Id);
        Assert.Equal(NotificationType.NewMessage, dto.Type);
        Assert.Equal(propertyId, dto.PropertyId);
        Assert.Equal(messageId, dto.RelatedEntityId);
        Assert.False(dto.IsRead);
    }

    private sealed class CapturingNotificationRepository : INotificationRepository
    {
        public List<Notification> Notifications { get; } = new();

        public Task AddAsync(Notification notification, CancellationToken ct = default)
        {
            // Do not assign notification.Id here.
            // In the production entity model, BaseEntity.Id has a non-public setter.
            // If the entity constructor initializes Id, we keep it. If not, this test still
            // verifies that the same persisted object is mapped and pushed.
            notification.CreatedAt = notification.CreatedAt == default
                ? DateTime.UtcNow
                : notification.CreatedAt;

            notification.UpdatedAt = notification.UpdatedAt == default
                ? notification.CreatedAt
                : notification.UpdatedAt;

            Notifications.Add(notification);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Notification>> GetUserNotificationsAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            var result = Notifications
                .Where(notification => notification.RecipientId == userId && !notification.IsDeleted)
                .OrderByDescending(notification => notification.CreatedAt)
                .Skip((Math.Max(1, page) - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult<IReadOnlyList<Notification>>(result);
        }

        public Task<int> GetUnreadCountAsync(
            Guid userId,
            CancellationToken ct = default)
        {
            var count = Notifications.Count(notification =>
                notification.RecipientId == userId &&
                !notification.IsRead &&
                !notification.IsDeleted);

            return Task.FromResult(count);
        }

        public Task<int> MarkAsReadAsync(
            Guid notificationId,
            Guid userId,
            CancellationToken ct = default)
        {
            var notification = Notifications.SingleOrDefault(item =>
                item.Id == notificationId &&
                item.RecipientId == userId &&
                !item.IsRead &&
                !item.IsDeleted);

            if (notification is null)
            {
                return Task.FromResult(0);
            }

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            notification.UpdatedAt = notification.ReadAt.Value;

            return Task.FromResult(1);
        }

        public Task<int> MarkAllAsReadAsync(
            Guid userId,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var affected = 0;

            foreach (var notification in Notifications.Where(item =>
                         item.RecipientId == userId &&
                         !item.IsRead &&
                         !item.IsDeleted))
            {
                notification.IsRead = true;
                notification.ReadAt = now;
                notification.UpdatedAt = now;
                affected++;
            }

            return Task.FromResult(affected);
        }

        public Task<bool> SoftDeleteAsync(
            Guid notificationId,
            Guid userId,
            CancellationToken ct = default)
        {
            var notification = Notifications.SingleOrDefault(item =>
                item.Id == notificationId &&
                item.RecipientId == userId &&
                !item.IsDeleted);

            if (notification is null)
            {
                return Task.FromResult(false);
            }

            notification.IsDeleted = true;
            notification.DeletedAt = DateTime.UtcNow;
            notification.UpdatedAt = notification.DeletedAt.Value;

            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(
            Guid notificationId,
            Guid userId,
            CancellationToken ct = default)
        {
            var removed = Notifications.RemoveAll(item =>
                item.Id == notificationId &&
                item.RecipientId == userId) > 0;

            return Task.FromResult(removed);
        }
    }

    private sealed class CapturingNotificationHubContext : IHubContext<NotificationHub>
    {
        public CapturingHubClients Clients { get; } = new();

        IHubClients IHubContext<NotificationHub>.Clients => Clients;

        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class CapturingHubClients : IHubClients
    {
        private readonly CapturingClientProxy _proxy;

        public CapturingHubClients()
        {
            _proxy = new CapturingClientProxy(this);
        }

        public string? LastGroupName { get; private set; }

        public string? LastMethodName { get; private set; }

        public IReadOnlyList<object?> LastArguments { get; private set; } = Array.Empty<object?>();

        public IClientProxy All => _proxy;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;

        public IClientProxy Client(string connectionId) => _proxy;

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;

        public IClientProxy Group(string groupName)
        {
            LastGroupName = groupName;
            return _proxy;
        }

        public IClientProxy GroupExcept(
            string groupName,
            IReadOnlyList<string> excludedConnectionIds)
        {
            LastGroupName = groupName;
            return _proxy;
        }

        public IClientProxy Groups(IReadOnlyList<string> groupNames)
        {
            LastGroupName = string.Join(",", groupNames);
            return _proxy;
        }

        public IClientProxy User(string userId) => _proxy;

        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;

        public void CaptureSend(
            string methodName,
            object?[] args)
        {
            LastMethodName = methodName;
            LastArguments = args;
        }
    }

    private sealed class CapturingClientProxy : IClientProxy
    {
        private readonly CapturingHubClients _clients;

        public CapturingClientProxy(CapturingHubClients clients)
        {
            _clients = clients;
        }

        public Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            _clients.CaptureSend(method, args);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
