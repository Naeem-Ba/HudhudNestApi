using HudhudNestApi.Application.Contact.DTOs;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Contact.Interfaces;

public interface IContactMessageRepository
{
    void Add(ContactMessage message);
    Task<ContactMessagesPageDto> GetPageAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ContactMessage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Remove(ContactMessage message);

    /// <summary>Ids of the active admin accounts, the recipients of the new-message notice.</summary>
    Task<IReadOnlyList<Guid>> ListAdminIdsAsync(CancellationToken ct = default);
}

