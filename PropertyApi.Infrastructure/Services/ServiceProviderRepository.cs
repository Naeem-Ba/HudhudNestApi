using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Domain.Services.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Services;

public sealed class ServiceProviderRepository : IServiceProviderRepository
{
    private readonly AppDbContext _db;
    public ServiceProviderRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceProvider provider, CancellationToken ct = default)
        => await _db.ServiceProviders.AddAsync(provider, ct);

    public async Task<ServiceProvider?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ServiceProviders.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ServiceProvider?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.ServiceProviders.FirstOrDefaultAsync(p => p.UserId == userId, ct);
}
