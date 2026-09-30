using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Domain.Bookings.Entities;
using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Bookings;

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
                // ✅ إصلاح: RescheduleProposed يُحسب أيضًا "معلّقًا" — الزائر ينتظر
                // ردّه على موعد بديل اقترحه المالك، فلا يجوز له فتح طلب زيارة
                // جديد للعقار نفسه بالتوازي أثناء ذلك.
                (v.Status == VisitStatus.Pending || v.Status == VisitStatus.RescheduleProposed), ct);

    public async Task<IReadOnlyList<VisitRequest>> GetByRequesterIdAsync(
        Guid requesterId, CancellationToken ct = default)
        => await _db.VisitRequests
            .Include(v => v.Property)
                .ThenInclude(p => p!.Images)
            .Include(v => v.Requester)
            .Where(v => v.RequesterId == requesterId)
            .OrderByDescending(v => v.ProposedAt)
            .ToListAsync(ct);

    // ✅ إصلاح: راجع تعليق IVisitRepository — يجمع زياراتي كزائر مع طلبات
    // الزيارة الواصلة لعقاراتي كمالك، باستعلام واحد بدل تجاهل الاتجاه الثاني.
    public async Task<IReadOnlyList<VisitRequest>> GetByRequesterOrOwnerIdAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.VisitRequests
            .Include(v => v.Property)
                .ThenInclude(p => p!.Images)
            .Include(v => v.Requester)
            .Where(v => v.RequesterId == userId || v.Property!.OwnerId == userId)
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

    public async Task<bool> HasCompletedVisitWithOwnerAsync(
        Guid raterId,
        Guid ratedOwnerId,
        CancellationToken ct = default)
    {
        return await _db.VisitRequests
            .AnyAsync(v =>
                v.RequesterId == raterId &&
                v.Status == VisitStatus.Completed &&
                v.Property!.OwnerId == ratedOwnerId,
                ct);
    }
}