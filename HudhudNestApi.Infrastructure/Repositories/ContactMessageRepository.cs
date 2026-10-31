using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Contact.DTOs;
using HudhudNestApi.Application.Contact.Interfaces;
using HudhudNestApi.Domain.Messaging.Entities;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Infrastructure.Repositories;

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
            .SkipPage(page, pageSize)
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

    public async Task<IReadOnlyList<Guid>> ListAdminIdsAsync(CancellationToken ct = default) =>
        await (from user in _db.Users.AsNoTracking()
               join userRole in _db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
               join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
               join account in _db.UserAccounts.AsNoTracking() on user.Id equals account.Id
               where role.Name == RoleNames.Admin && !account.IsDeleted
               select user.Id).ToListAsync(ct);
}
