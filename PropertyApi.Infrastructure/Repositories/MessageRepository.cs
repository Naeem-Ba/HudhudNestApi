using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class MessageRepository : IMessageRepository
{
    private readonly AppDbContext _db;

    public MessageRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(Message message, CancellationToken ct = default)
        => await _db.Messages.AddAsync(message, ct);

    public async Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Messages
            .Include(m => m.Sender)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<PagedResult<Message>> GetByPropertyAsync(
        Guid propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Messages
            .Where(m => m.PropertyId == propertyId)
            .Include(m => m.Sender)
            .OrderByDescending(m => m.CreatedAt);

        var total = await query.CountAsync(ct);
        var safePageSize = Math.Min(pageSize, 50);

        var items = await query
            .Skip((page - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return new PagedResult<Message>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = safePageSize
        };
    }

    public async Task<IReadOnlyList<Message>> GetConversationAsync(
        Guid propertyId,
        Guid senderId,
        Guid receiverId,
        CancellationToken ct = default)
    {
        return await _db.Messages
            .Where(m => m.PropertyId == propertyId &&
                        ((m.SenderId == senderId && m.ReceiverId == receiverId) ||
                         (m.SenderId == receiverId && m.ReceiverId == senderId)))
            .Include(m => m.Sender)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
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
    public async Task<int> CountUnreadAsync(Guid receiverId, CancellationToken ct = default)
        => await _db.Messages
            .CountAsync(m => m.ReceiverId == receiverId && !m.IsRead, ct);

    public async Task MarkAsReadAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await _db.Messages.FindAsync(new object[] { messageId }, ct);
        if (message is not null)
        {
            message.IsRead = true;
            message.ReadAt = DateTime.UtcNow;
        }
    }

    public void Remove(Message message)
        => _db.Messages.Remove(message); // intercepted as soft delete by AppDbContext
}
