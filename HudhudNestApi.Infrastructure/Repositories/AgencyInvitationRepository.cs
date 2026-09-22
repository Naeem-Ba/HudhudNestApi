using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Agencies.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class AgencyInvitationRepository : IAgencyInvitationRepository
{
    private readonly AppDbContext _db;

    public AgencyInvitationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AgencyInvitation invitation, CancellationToken ct = default)
        => await _db.AgencyInvitations.AddAsync(invitation, ct);

    public async Task<AgencyInvitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default)
        => await _db.AgencyInvitations.FirstOrDefaultAsync(i => i.Id == invitationId, ct);

    public async Task<AgencyInvitation?> GetPendingAsync(
        Guid agencyId,
        Guid targetUserId,
        CancellationToken ct = default)
        => await _db.AgencyInvitations.FirstOrDefaultAsync(
            i => i.AgencyId == agencyId
                && i.TargetUserId == targetUserId
                && i.Status == AgencyInvitationStatus.Pending,
            ct);

    public async Task<IReadOnlyList<AgencyInvitation>> GetActionableForTargetAsync(
        Guid targetUserId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.AgencyInvitations
            .AsNoTracking()
            .Where(i => i.TargetUserId == targetUserId
                && i.Status == AgencyInvitationStatus.Pending
                && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task ReloadAsync(AgencyInvitation invitation, CancellationToken ct = default)
        => await _db.Entry(invitation).ReloadAsync(ct);
}
