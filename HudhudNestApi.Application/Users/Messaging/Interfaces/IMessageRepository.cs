using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.Messaging.DTOs;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Users.Messaging.Interfaces;

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
    /// How many messages <paramref name="senderId"/> has sent about one listing since
    /// <paramref name="sinceUtc"/> (to anyone). Basis of the per-listing sending limit.
    /// </summary>
    Task<int> CountSentAboutPropertySinceAsync(
        Guid propertyId,
        Guid senderId,
        DateTime sinceUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="receiverId"/> still has an unread message from <paramref name="senderId"/>
    /// about the listing that was created at or after <paramref name="sinceUtc"/>.
    /// </summary>
    Task<bool> HasRecentUnreadAsync(
        Guid propertyId,
        Guid senderId,
        Guid receiverId,
        DateTime sinceUtc,
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
