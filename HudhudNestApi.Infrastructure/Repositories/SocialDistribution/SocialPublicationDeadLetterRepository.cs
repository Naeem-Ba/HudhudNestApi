using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories.SocialDistribution;

public sealed class SocialPublicationDeadLetterRepository : ISocialPublicationDeadLetterRepository
{
    private readonly AppDbContext _db;

    public SocialPublicationDeadLetterRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SocialPublicationDeadLetter deadLetter, CancellationToken ct = default) =>
        await _db.SocialPublicationDeadLetters.AddAsync(deadLetter, ct);

    public Task<SocialPublicationDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SocialPublicationDeadLetters.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<bool> ExistsUnresolvedForPublicationAsync(Guid publicationId, CancellationToken ct = default) =>
        _db.SocialPublicationDeadLetters.AnyAsync(d => d.PublicationId == publicationId && d.ResolvedAt == null, ct);

    public async Task<PagedResult<SocialPublicationDeadLetter>> GetPagedAsync(bool? resolved, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.SocialPublicationDeadLetters.AsNoTracking().AsQueryable();

        if (resolved is not null)
            query = resolved.Value ? query.Where(d => d.ResolvedAt != null) : query.Where(d => d.ResolvedAt == null);

        var totalCount = await query.CountAsync(ct);
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(d => d.FailedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        return new PagedResult<SocialPublicationDeadLetter>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    public void Update(SocialPublicationDeadLetter deadLetter) => _db.SocialPublicationDeadLetters.Update(deadLetter);
}
