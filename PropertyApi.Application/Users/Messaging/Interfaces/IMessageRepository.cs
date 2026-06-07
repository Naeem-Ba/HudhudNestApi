using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Messaging.Entities;

namespace PropertyApi.Application.Users.Messaging.Interfaces;

public interface IMessageRepository
{
    // Create
    Task AddAsync(Message message, CancellationToken ct = default);

    // Read
    Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Get all messages for a property (paged).</summary>
    Task<PagedResult<Message>> GetByPropertyAsync(
        Guid propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>Get conversation thread between two users about a property.</summary>
    Task<IReadOnlyList<Message>> GetConversationAsync(
        Guid propertyId,
        Guid senderId,
        Guid receiverId,
        CancellationToken ct = default);
    Task<bool> ConversationExistsAsync(
    Guid propertyId,
    Guid firstUserId,
    Guid secondUserId,
    CancellationToken ct = default);

    /// <summary>Count unread messages for a user.</summary>
    Task<int> CountUnreadAsync(Guid receiverId, CancellationToken ct = default);

    // Update
    Task MarkAsReadAsync(Guid messageId, CancellationToken ct = default);

    // Soft delete
    void Remove(Message message);
}
