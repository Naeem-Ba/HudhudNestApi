using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

public interface IPropertyRepository
{
    // Create
    Task AddAsync(Property property, CancellationToken ct = default);

    // Read
    Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Property?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Property?> GetPublishedByIdWithDetailsAsync(Guid id,CancellationToken ct = default);
    Task<PagedResult<Property>> GetPagedAsync(PropertyFilterDto filter, CancellationToken ct = default);
    Task<IReadOnlyList<Property>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    // Update — repository just tracks, UnitOfWork saves
    void Update(Property property);

    // Delete — soft delete via MarkAsDeleted() domain method
    void Remove(Property property);
}
