using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

public sealed class ServiceProviderEventRepository : IServiceProviderEventRepository
{
    private readonly AppDbContext _db;
    public ServiceProviderEventRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceProviderEvent entry, CancellationToken ct = default)
        => await _db.ServiceProviderEvents.AddAsync(entry, ct);

    public async Task<IReadOnlyList<ServiceProviderEvent>> ListForApplicationAsync(
        Guid applicationId, bool includeInternal, CancellationToken ct = default)
        => await _db.ServiceProviderEvents
            .AsNoTracking()
            .Where(e => e.ApplicationId == applicationId && (includeInternal || !e.IsInternal))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceProviderEvent>> ListForProviderAsync(
        Guid serviceProviderId, CancellationToken ct = default)
    {
        var applicationIds = _db.ServiceProviderApplications
            .Where(a => a.ServiceProviderId == serviceProviderId)
            .Select(a => (Guid?)a.Id);

        return await _db.ServiceProviderEvents
            .AsNoTracking()
            .Where(e => e.ServiceProviderId == serviceProviderId || applicationIds.Contains(e.ApplicationId))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
    }
}
