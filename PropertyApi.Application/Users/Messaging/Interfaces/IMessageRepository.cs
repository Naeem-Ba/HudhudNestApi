using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Users.Messaging.DTOs;
using PropertyApi.Domain.Messaging.Entities;

namespace PropertyApi.Application.Users.Messaging.Interfaces;

public interface IMessageRepository
{
    // Create
    Task AddAsync(Message message, CancellationToken ct = default);

    // Read
    Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get all messages for a property, paged in the database.
    /// </summary>
    Task<PagedResult<Message>> GetByPropertyAsync(
        Guid propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Get conversation thread between two users about a property, paged in the database.
    /// </summary>
    Task<PagedResult<Message>> GetConversationAsync(
        Guid propertyId,
        Guid firstUserId,
        Guid secondUserId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Check whether a conversation exists between two users about a property.
    /// </summary>
    Task<bool> ConversationExistsAsync(
        Guid propertyId,
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken ct = default);

    /// <summary>
    /// يتحقق من وجود أي رسالة بين مستخدمَين، عبر كل العقارات (بعكس
    /// <see cref="ConversationExistsAsync"/> المرتبطة بعقار واحد) — مطلوب
    /// لشرط "زيارة مكتملة أو مراسلة" قبل السماح بتقييم مستخدم آخر.
    /// </summary>
    Task<bool> HasAnyConversationAsync(
        Guid userId1,
        Guid userId2,
        CancellationToken ct = default);

    /// <summary>
    /// The user's inbox: one row per (property, other participant) pair the user
    /// has exchanged messages about, newest activity first, paged in the database.
    /// Only messages where the user is sender or receiver are considered.
    /// </summary>
    Task<PagedResult<ConversationSummaryDto>> GetConversationSummariesAsync(
        Guid userId,
        Guid? propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Tracked unread messages sent by <paramref name="senderId"/> to
    /// <paramref name="receiverId"/> about a property (for mark-as-read).
    /// </summary>
    Task<IReadOnlyList<Message>> GetUnreadInConversationAsync(
        Guid propertyId,
        Guid receiverId,
        Guid senderId,
        CancellationToken ct = default);

    /// <summary>
    /// Count unread messages for a user.
    /// </summary>
    Task<int> CountUnreadAsync(Guid receiverId, CancellationToken ct = default);

    // Update
    Task MarkAsReadAsync(Guid messageId, CancellationToken ct = default);

    // Delete
    void Remove(Message message);
}
