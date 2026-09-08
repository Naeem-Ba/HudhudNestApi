using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Domain.Marketing.Entities;

namespace PropertyApi.Application.Marketing.Interfaces;

public interface ILeadRepository
{
    void Add(Lead lead);

    Task<Lead?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<LeadsPageDto> GetPageAsync(
        int page,
        int pageSize,
        string? source,
        string? userType,
        CancellationToken ct = default);
}
