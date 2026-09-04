using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Investments;

public sealed class InvestmentWatchlistRepository : IInvestmentWatchlistRepository
{
    private readonly AppDbContext _db;

    public InvestmentWatchlistRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentWatchlistItems.AnyAsync(w => w.UserId == userId && w.InvestmentProjectId == investmentProjectId, ct);

    public Task<InvestmentWatchlistItem?> GetAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentWatchlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.InvestmentProjectId == investmentProjectId, ct);

    public Task<bool> ProjectExistsAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentProjects.AnyAsync(p => p.Id == investmentProjectId, ct);

    public void Add(InvestmentWatchlistItem item) => _db.InvestmentWatchlistItems.Add(item);

    public void Remove(InvestmentWatchlistItem item) => _db.InvestmentWatchlistItems.Remove(item);

    /// <summary>Card projection regardless of the project's current status — this is the owning
    /// user's own saved list, not public exposure of the project.</summary>
    public async Task<IReadOnlyList<InvestmentWatchlistDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var query =
            from item in _db.InvestmentWatchlistItems.AsNoTracking()
            join project in _db.InvestmentProjects.AsNoTracking() on item.InvestmentProjectId equals project.Id
            join property in _db.Properties.AsNoTracking() on project.PropertyId equals property.Id
            where item.UserId == userId
            orderby item.CreatedAt descending
            select new InvestmentWatchlistDto(
                item.InvestmentProjectId,
                item.CreatedAt,
                new InvestmentProjectListDto(
                    project.Id,
                    project.Title,
                    project.ShortDescription,
                    project.ProjectType,
                    project.Status,
                    property.City,
                    property.CountryCode,
                    property.Images.Where(i => i.IsMain).Select(i => i.Url).FirstOrDefault(),
                    project.TargetAmount,
                    project.RaisedAmount,
                    project.MinimumInvestment,
                    project.MaximumInvestment,
                    project.Currency,
                    project.InvestmentTermMonths,
                    project.ExpectedReturnMin,
                    project.ExpectedReturnMax,
                    project.RiskLevel,
                    project.PublishedAt));

        return await query.ToListAsync(ct);
    }
}
