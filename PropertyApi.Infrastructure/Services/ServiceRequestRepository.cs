using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Domain.Services.Entities;
using PropertyApi.Domain.Services.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Services;

public sealed class ServiceRequestRepository : IServiceRequestRepository
{
    private readonly AppDbContext _db;
    public ServiceRequestRepository(AppDbContext db) => _db = db;

    private IQueryable<ServiceRequest> WithNavigations() => _db.ServiceRequests
        .Include(r => r.Property)
            .ThenInclude(p => p!.Images)
        .Include(r => r.Requester)
        .Include(r => r.ServiceProvider)
        .Include(r => r.ServiceOffering);

    public async Task AddAsync(ServiceRequest request, CancellationToken ct = default)
        => await _db.ServiceRequests.AddAsync(request, ct);

    public async Task<ServiceRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await WithNavigations().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<ServiceRequest>> GetByRequesterIdAsync(
        Guid requesterId, CancellationToken ct = default)
        => await WithNavigations()
            .Where(r => r.RequesterId == requesterId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceRequest>> GetByServiceProviderIdAsync(
        Guid serviceProviderId, CancellationToken ct = default)
        => await WithNavigations()
            .Where(r => r.ServiceProviderId == serviceProviderId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> HasActiveRequestAsync(
        Guid propertyId, Guid requesterId, Guid serviceOfferingId, CancellationToken ct = default)
        => await _db.ServiceRequests.AnyAsync(r =>
            r.PropertyId == propertyId &&
            r.RequesterId == requesterId &&
            r.ServiceOfferingId == serviceOfferingId &&
            r.Status != ServiceRequestStatus.Completed &&
            r.Status != ServiceRequestStatus.Reviewed &&
            r.Status != ServiceRequestStatus.Rejected &&
            r.Status != ServiceRequestStatus.Cancelled,
            ct);
}
