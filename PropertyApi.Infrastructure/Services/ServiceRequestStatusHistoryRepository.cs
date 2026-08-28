using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Domain.Services.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Services;

public sealed class ServiceRequestStatusHistoryRepository : IServiceRequestStatusHistoryRepository
{
    private readonly AppDbContext _db;
    public ServiceRequestStatusHistoryRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceRequestStatusHistory entry, CancellationToken ct = default)
        => await _db.ServiceRequestStatusHistories.AddAsync(entry, ct);

    public async Task<IReadOnlyList<ServiceRequestStatusHistory>> GetByServiceRequestIdAsync(
        Guid serviceRequestId, CancellationToken ct = default)
        => await _db.ServiceRequestStatusHistories
            .Where(h => h.ServiceRequestId == serviceRequestId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);
}
