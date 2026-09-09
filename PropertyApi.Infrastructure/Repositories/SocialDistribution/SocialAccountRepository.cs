using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories.SocialDistribution;

public sealed class SocialAccountRepository : ISocialAccountRepository
{
    private readonly AppDbContext _db;

    public SocialAccountRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SocialAccount account, CancellationToken ct = default) =>
        await _db.SocialAccounts.AddAsync(account, ct);

    public Task<SocialAccount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<bool> ExternalAccountExistsAsync(SocialPlatform platform, string externalAccountId, CancellationToken ct = default) =>
        _db.SocialAccounts.AnyAsync(a => a.Platform == platform && a.ExternalAccountId == externalAccountId, ct);

    public async Task<PagedResult<SocialAccount>> GetPagedAsync(SocialAccountFilterDto filter, CancellationToken ct = default)
    {
        var query = _db.SocialAccounts.AsNoTracking().AsQueryable();

        if (filter.Platform is not null)
            query = query.Where(a => a.Platform == filter.Platform);

        if (filter.GovernorateId is not null)
            query = query.Where(a => a.GovernorateId == filter.GovernorateId);

        if (filter.Status is not null)
            query = query.Where(a => a.Status == filter.Status);

        var totalCount = await query.CountAsync(ct);

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<SocialAccount>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public void Update(SocialAccount account) => _db.SocialAccounts.Update(account);
}
