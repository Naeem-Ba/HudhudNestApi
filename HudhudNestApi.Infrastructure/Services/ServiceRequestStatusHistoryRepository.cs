using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

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
