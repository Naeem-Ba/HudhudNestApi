using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.Messaging.Interfaces;
using HudhudNestApi.Domain.Messaging.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

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