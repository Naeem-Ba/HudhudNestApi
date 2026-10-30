using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Investments;

public sealed class InvestmentWatchlistRepository : IInvestmentWatchlistRepository
{
    private readonly AppDbContext _db;

    public InvestmentWatchlistRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentWatchlistItems.AnyAsync(w => w.UserId == userId && w.InvestmentProjectId == investmentProjectId, ct);

    public Task<InvestmentWatchlistItem?> GetAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentWatchlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.InvestmentProjectId == investmentProjectId, ct);

    public Task<bool> ProjectExistsAndPublishedAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentProjects.AnyAsync(p => p.Id == investmentProjectId && p.Status == InvestmentProjectStatus.Published, ct);

    public void Add(InvestmentWatchlistItem item) => _db.InvestmentWatchlistItems.Add(item);

    public void Remove(InvestmentWatchlistItem item) => _db.InvestmentWatchlistItems.Remove(item);

    /// <summary>The owning user's saved list. P10-D05: only projects that are publicly visible right now
    /// (Published, property alive) are shown — a suspended/closed/withdrawn project must not keep leaking its card
    /// (title, amounts) through the list. The row itself is kept, so the project reappears if it is re-published.</summary>
    public async Task<IReadOnlyList<InvestmentWatchlistDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var query =
            from item in _db.InvestmentWatchlistItems.AsNoTracking()
            join project in _db.InvestmentProjects.AsNoTracking() on item.InvestmentProjectId equals project.Id
            join property in _db.Properties.AsNoTracking() on project.PropertyId equals property.Id
            where item.UserId == userId && project.Status == InvestmentProjectStatus.Published
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
