using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories.SocialDistribution;

public sealed class SocialPublicationStatusHistoryRepository : ISocialPublicationStatusHistoryRepository
{
    private readonly AppDbContext _db;

    public SocialPublicationStatusHistoryRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SocialPublicationStatusHistory history, CancellationToken ct = default) =>
        await _db.SocialPublicationStatusHistories.AddAsync(history, ct);

    public async Task<IReadOnlyList<SocialPublicationStatusHistory>> GetByPublicationIdAsync(Guid publicationId, CancellationToken ct = default) =>
        await _db.SocialPublicationStatusHistories
            .AsNoTracking()
            .Where(h => h.SocialPublicationId == publicationId)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(ct);
}
