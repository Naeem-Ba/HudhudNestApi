using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Plans.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class PlanRequestRepository : IPlanRequestRepository
{
    private readonly AppDbContext _db;

    public PlanRequestRepository(AppDbContext db)
    {
        _db = db;
    }

    // Tracked on purpose: every caller goes on to resolve or supersede the row and expects
    // SaveChangesAsync to persist it.
    public async Task<PlanRequest?> GetPendingForUserAsync(Guid userId, CancellationToken ct = default)
        => await _db.PlanRequests
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Status == PlanRequestStatus.Pending, ct);

    public async Task<PlanRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.PlanRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(PlanRequest request, CancellationToken ct = default)
        => await _db.PlanRequests.AddAsync(request, ct);

    public async Task ReloadAsync(PlanRequest request, CancellationToken ct = default)
        => await _db.Entry(request).ReloadAsync(ct);
}
