using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Contact.DTOs;
using PropertyApi.Application.Contact.Interfaces;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class ContactMessageRepository : IContactMessageRepository
{
    private readonly AppDbContext _db;

    public ContactMessageRepository(AppDbContext db)
        => _db = db;

    public void Add(ContactMessage message)
    {
        _db.ContactMessages.Add(message);
    }

    public async Task<ContactMessagesPageDto> GetPageAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.ContactMessages
            .AsNoTracking()
            .OrderByDescending(message => message.CreatedAt);

        var total = await query.CountAsync(ct);

        var data = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new ContactMessageDto(
                message.Id,
                message.Name,
                message.Email,
                message.Subject,
                message.Body,
                message.IsRead,
                message.CreatedAt))
            .ToListAsync(ct);

        return new ContactMessagesPageDto(total, page, pageSize, data);
    }

    public Task<ContactMessage?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _db.ContactMessages.FindAsync([id], ct).AsTask();
    }

    public void Remove(ContactMessage message)
    {
        _db.ContactMessages.Remove(message);
    }
}
