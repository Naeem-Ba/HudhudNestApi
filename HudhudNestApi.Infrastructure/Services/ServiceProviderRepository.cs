using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

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
