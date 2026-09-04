using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Investments;

public sealed class InvestmentInterestRepository : IInvestmentInterestRepository
{
    private readonly AppDbContext _db;

    public InvestmentInterestRepository(AppDbContext db) => _db = db;

    public Task<InvestmentInterest?> GetAsync(Guid userId, Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentInterests.FirstOrDefaultAsync(i => i.UserId == userId && i.InvestmentProjectId == investmentProjectId, ct);

    /// <summary>A user may only express interest in a project that actually exists AND is
    /// currently Published — never in a Draft/UnderReview/Closed one (Phase 1 spec §30).</summary>
    public Task<bool> ProjectExistsAndPublishedAsync(Guid investmentProjectId, CancellationToken ct = default) =>
        _db.InvestmentProjects.AnyAsync(p => p.Id == investmentProjectId && p.Status == InvestmentProjectStatus.Published, ct);

    public void Add(InvestmentInterest interest) => _db.InvestmentInterests.Add(interest);

    public async Task<IReadOnlyList<InvestmentInterestDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var query =
            from interest in _db.InvestmentInterests.AsNoTracking()
            join project in _db.InvestmentProjects.AsNoTracking() on interest.InvestmentProjectId equals project.Id
            where interest.UserId == userId
            orderby interest.CreatedAt descending
            select new InvestmentInterestDto(interest.Id, interest.InvestmentProjectId, project.Title, interest.Status, interest.CreatedAt);

        return await query.ToListAsync(ct);
    }
}
