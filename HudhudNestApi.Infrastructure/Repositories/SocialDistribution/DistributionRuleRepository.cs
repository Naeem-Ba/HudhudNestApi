using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories.SocialDistribution;

public sealed class DistributionRuleRepository : IDistributionRuleRepository
{
    private readonly AppDbContext _db;

    public DistributionRuleRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(DistributionRule rule, CancellationToken ct = default) =>
        await _db.DistributionRules.AddAsync(rule, ct);

    public Task<DistributionRule?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.DistributionRules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<PagedResult<DistributionRule>> GetPagedAsync(DistributionRuleFilterDto filter, CancellationToken ct = default)
    {
        var query = _db.DistributionRules.AsNoTracking().AsQueryable();

        if (!filter.IncludeArchived)
            query = query.Where(r => !r.IsArchived);

        if (filter.ProvinceId is not null)
            query = query.Where(r => r.ProvinceId == filter.ProvinceId);

        if (filter.PropertyTypeId is not null)
            query = query.Where(r => r.PropertyTypeId == filter.PropertyTypeId);

        if (filter.TransactionType is not null)
            query = query.Where(r => r.TransactionType == filter.TransactionType);

        if (filter.SocialAccountId is not null)
            query = query.Where(r => r.SocialAccountId == filter.SocialAccountId);

        if (filter.IsActive is not null)
            query = query.Where(r => r.IsActive == filter.IsActive);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            query = query.Where(r => EF.Functions.ILike(r.Name, $"%{filter.SearchText}%"));

        var totalCount = await query.CountAsync(ct);

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<DistributionRule>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<IReadOnlyList<DistributionRule>> GetActiveCandidatesAsync(
        int? governorateId, int? propertyTypeId, ListingType listingType, DateTime utcNow, CancellationToken ct = default) =>
        await _db.DistributionRules
            .Where(r =>
                r.IsActive && !r.IsArchived &&
                (r.StartAt == null || r.StartAt <= utcNow) &&
                (r.EndAt == null || r.EndAt >= utcNow) &&
                (r.ProvinceId == null || r.ProvinceId == governorateId) &&
                (r.PropertyTypeId == null || r.PropertyTypeId == propertyTypeId) &&
                (r.TransactionType == null || r.TransactionType == listingType))
            .ToListAsync(ct);

    public void Update(DistributionRule rule) => _db.DistributionRules.Update(rule);
}
