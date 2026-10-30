using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

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

    public async Task<(IReadOnlyList<ServiceProvider> Items, int Total)> SearchAsync(
        ServiceProviderFilter filter, CancellationToken ct = default)
    {
        var query = _db.ServiceProviders.AsNoTracking();

        if (filter.Status is { } status)
            query = query.Where(p => p.Status == status);

        if (filter.Category is { } category)
        {
            var name = category.ToString();
            query = query.Where(p => p.ApprovedCategories.Contains(name));
        }

        if (filter.GovernorateId is { } governorateId)
            query = query.Where(p => p.CoverageGovernorateIds.Contains(governorateId));

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{ServiceProviderApplicationRepository.EscapeLike(filter.Search.Trim())}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.DisplayName, pattern, "\\") ||
                (p.ContactEmail != null && EF.Functions.ILike(p.ContactEmail, pattern, "\\")) ||
                (p.ContactPhone != null && EF.Functions.ILike(p.ContactPhone, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.DisplayName)
            .SkipPage(filter.Page, filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
