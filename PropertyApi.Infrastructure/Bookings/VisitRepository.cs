using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Domain.Bookings.Entities;
using PropertyApi.Domain.Bookings.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Bookings;

public sealed class VisitRepository : IVisitRepository
{
    private readonly AppDbContext _db;
    public VisitRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(VisitRequest visit, CancellationToken ct = default)
        => await _db.VisitRequests.AddAsync(visit, ct);

    public async Task<VisitRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.VisitRequests
            .Include(v => v.Property)
            .Include(v => v.Requester)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<bool> HasPendingVisitAsync(
        Guid propertyId, Guid requesterId, CancellationToken ct = default)
        => await _db.VisitRequests
            .AnyAsync(v =>
                v.PropertyId == propertyId &&
                v.RequesterId == requesterId &&
                v.Status == VisitStatus.Pending, ct);

    public async Task<IReadOnlyList<VisitRequest>> GetByRequesterIdAsync(
        Guid requesterId, CancellationToken ct = default)
        => await _db.VisitRequests
            .Include(v => v.Property)
                .ThenInclude(p => p!.Images)
            .Include(v => v.Requester)
            .Where(v => v.RequesterId == requesterId)
            .OrderByDescending(v => v.ProposedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<VisitRequest>> GetByPropertyIdAsync(
        Guid propertyId, CancellationToken ct = default)
        => await _db.VisitRequests
            .Include(v => v.Requester)
            .Where(v => v.PropertyId == propertyId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> HasCompletedVisitAsync(
    Guid propertyId,
    Guid requesterId,
    CancellationToken cancellationToken = default)
    {
        return await _db.VisitRequests
            .AnyAsync(v =>
                v.PropertyId == propertyId &&
                v.RequesterId == requesterId &&
                v.Status == VisitStatus.Completed,
                cancellationToken);
    }
}