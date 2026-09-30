using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Investments;

public sealed class InvestmentUpdateRepository : IInvestmentUpdateRepository
{
    private readonly AppDbContext _db;

    public InvestmentUpdateRepository(AppDbContext db) => _db = db;

    public void Add(InvestmentUpdate update) => _db.InvestmentUpdates.Add(update);

    /// <summary>Published-project gate — same reasoning as the other public sub-resource
    /// repositories (Phase 1 §19/§20).</summary>
    public async Task<IReadOnlyList<InvestmentUpdateDto>> GetForProjectAsync(Guid investmentProjectId, CancellationToken ct = default)
    {
        var query =
            from update in _db.InvestmentUpdates.AsNoTracking()
            join project in _db.InvestmentProjects.AsNoTracking() on update.InvestmentProjectId equals project.Id
            where update.InvestmentProjectId == investmentProjectId && project.Status == InvestmentProjectStatus.Published
            orderby update.PublishedAt descending
            select new InvestmentUpdateDto(update.Id, update.Title, update.Content, update.UpdateType, update.PublishedAt);

        return await query.ToListAsync(ct);
    }
}
