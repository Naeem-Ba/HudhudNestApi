using PropertyApi.Application.Contact.DTOs;
using PropertyApi.Domain.Messaging.Entities;

namespace PropertyApi.Application.Contact.Interfaces;

public interface IContactMessageRepository
{
    void Add(ContactMessage message);
    Task<ContactMessagesPageDto> GetPageAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ContactMessage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Remove(ContactMessage message);
}
