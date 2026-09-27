using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Users.Messaging.DTOs;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class MessageRepository : IMessageRepository
{
    private readonly AppDbContext _db;

    public MessageRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await _db.Messages.AddAsync(message, ct);
    }

    public async Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Messages
            .Include(message => message.Sender)
            .FirstOrDefaultAsync(message => message.Id == id, ct);
    }

    public async Task<PagedResult<Message>> GetByPropertyAsync(
        Guid propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.Messages
            .AsNoTracking()
            .Where(message => message.PropertyId == propertyId)
            .Include(message => message.Sender);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(message => message.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return new PagedResult<Message>
        {
            Items = items,
            TotalCount = total,
            Page = safePage,
            PageSize = safePageSize
        };
    }

    public async Task<PagedResult<Message>> GetConversationAsync(
        Guid propertyId,
        Guid firstUserId,
        Guid secondUserId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.Messages
            .AsNoTracking()
            .Where(message =>
                message.PropertyId == propertyId &&
                (
                    (message.SenderId == firstUserId &&
                     message.ReceiverId == secondUserId) ||
                    (message.SenderId == secondUserId &&
                     message.ReceiverId == firstUserId)
                ))
            .Include(message => message.Sender);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(message => message.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return new PagedResult<Message>
        {
            Items = items,
            TotalCount = total,
            Page = safePage,
            PageSize = safePageSize
        };
    }

    public async Task<bool> ConversationExistsAsync(
        Guid propertyId,
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken ct = default)
    {
        return await _db.Messages.AnyAsync(
            message =>
                message.PropertyId == propertyId &&
                (
                    (message.SenderId == firstUserId &&
                     message.ReceiverId == secondUserId) ||
                    (message.SenderId == secondUserId &&
                     message.ReceiverId == firstUserId)
                ),
            ct);
    }

    public async Task<bool> HasAnyConversationAsync(
        Guid userId1,
        Guid userId2,
        CancellationToken ct = default)
    {
        return await _db.Messages.AnyAsync(
            message =>
                (message.SenderId == userId1 && message.ReceiverId == userId2) ||
                (message.SenderId == userId2 && message.ReceiverId == userId1),
            ct);
    }

    public async Task<PagedResult<ConversationSummaryDto>> GetConversationSummariesAsync(
        Guid userId,
        Guid? propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 50);

        var mine = _db.Messages
            .AsNoTracking()
            .Where(message => message.SenderId == userId || message.ReceiverId == userId);

        if (propertyId.HasValue)
        {
            mine = mine.Where(message => message.PropertyId == propertyId.Value);
        }

        // One group per (property, counterpart). Grouping, counting and paging
        // all run in the database; only the requested page is materialized.
        var groups = mine
            .Select(message => new
            {
                message.PropertyId,
                OtherUserId = message.SenderId == userId ? message.ReceiverId : message.SenderId,
                message.CreatedAt,
                Unread = message.ReceiverId == userId && !message.IsRead ? 1 : 0
            })
            .GroupBy(row => new { row.PropertyId, row.OtherUserId })
            .Select(group => new
            {
                group.Key.PropertyId,
                group.Key.OtherUserId,
                LastMessageAt = group.Max(row => row.CreatedAt),
                UnreadCount = group.Sum(row => row.Unread)
            });

        var total = await groups.CountAsync(ct);

        var pageRows = await groups
            .OrderByDescending(group => group.LastMessageAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        if (pageRows.Count == 0)
        {
            return new PagedResult<ConversationSummaryDto>
            {
                Items = Array.Empty<ConversationSummaryDto>(),
                TotalCount = total,
                Page = safePage,
                PageSize = safePageSize
            };
        }

        var propertyIds = pageRows.Select(row => row.PropertyId).Distinct().ToList();
        var otherUserIds = pageRows.Select(row => row.OtherUserId).Distinct().ToList();
        var lastTimes = pageRows.Select(row => row.LastMessageAt).Distinct().ToList();

        // The latest message of each conversation on this page: candidates are
        // narrowed by property + timestamp in SQL, then matched per pair below.
        var lastMessages = await mine
            .Where(message =>
                propertyIds.Contains(message.PropertyId) &&
                lastTimes.Contains(message.CreatedAt))
            .Select(message => new
            {
                message.PropertyId,
                message.SenderId,
                message.ReceiverId,
                message.Content,
                message.CreatedAt
            })
            .ToListAsync(ct);

        // IgnoreQueryFilters: a conversation about a listing that was later
        // soft-deleted stays readable in the inbox (with its title).
        var properties = await _db.Properties
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(property => propertyIds.Contains(property.Id))
            .Select(property => new
            {
                property.Id,
                property.Title,
                property.OwnerId,
                ImageUrl = property.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.CreatedAt)
                    .Select(image => image.Url)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(property => property.Id, ct);

        var users = await _db.UserAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(account => otherUserIds.Contains(account.Id))
            .Select(account => new
            {
                account.Id,
                account.DisplayName,
                account.FirstName,
                account.LastName,
                account.ProfileImageUrl
            })
            .ToDictionaryAsync(account => account.Id, ct);

        var items = pageRows
            .Select(row =>
            {
                var last = lastMessages.FirstOrDefault(message =>
                    message.PropertyId == row.PropertyId &&
                    message.CreatedAt == row.LastMessageAt &&
                    (message.SenderId == row.OtherUserId || message.ReceiverId == row.OtherUserId));

                properties.TryGetValue(row.PropertyId, out var property);
                users.TryGetValue(row.OtherUserId, out var other);

                var otherName = other is null
                    ? string.Empty
                    : !string.IsNullOrWhiteSpace(other.DisplayName)
                        ? other.DisplayName
                        : $"{other.FirstName} {other.LastName}".Trim();

                return new ConversationSummaryDto
                {
                    PropertyId = row.PropertyId,
                    PropertyTitle = property?.Title ?? string.Empty,
                    PropertyImageUrl = property?.ImageUrl,
                    IsPropertyOwner = property is not null && property.OwnerId == userId,
                    OtherUserId = row.OtherUserId,
                    OtherUserDisplayName = otherName,
                    OtherUserImageUrl = other?.ProfileImageUrl,
                    LastMessageSenderId = last?.SenderId ?? Guid.Empty,
                    LastMessageContent = last?.Content ?? string.Empty,
                    LastMessageAt = row.LastMessageAt,
                    UnreadCount = row.UnreadCount
                };
            })
            .ToList();

        return new PagedResult<ConversationSummaryDto>
        {
            Items = items,
            TotalCount = total,
            Page = safePage,
            PageSize = safePageSize
        };
    }

    public async Task<IReadOnlyList<Message>> GetUnreadInConversationAsync(
        Guid propertyId,
        Guid receiverId,
        Guid senderId,
        CancellationToken ct = default)
    {
        return await _db.Messages
            .Where(message =>
                message.PropertyId == propertyId &&
                message.ReceiverId == receiverId &&
                message.SenderId == senderId &&
                !message.IsRead)
            .ToListAsync(ct);
    }

    public async Task<int> CountUnreadAsync(
        Guid receiverId,
        CancellationToken ct = default)
    {
        return await _db.Messages
            .CountAsync(
                message =>
                    message.ReceiverId == receiverId &&
                    !message.IsRead,
                ct);
    }

    public async Task MarkAsReadAsync(
        Guid messageId,
        CancellationToken ct = default)
    {
        var message = await _db.Messages.FindAsync(
            new object[] { messageId },
            ct);

        if (message is null)
            return;

        message.IsRead = true;
        message.ReadAt = DateTime.UtcNow;
    }

    public void Remove(Message message)
    {
        _db.Messages.Remove(message);
    }
}