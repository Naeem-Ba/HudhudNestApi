using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories.SocialDistribution;

public sealed class SocialChannelRepository : ISocialChannelRepository
{
    private readonly AppDbContext _db;

    public SocialChannelRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SocialChannel channel, CancellationToken ct = default) =>
        await _db.SocialChannels.AddAsync(channel, ct);

    public Task<SocialChannel?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SocialChannels.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<SocialChannel?> GetByPlatformAsync(SocialPlatform platform, CancellationToken ct = default) =>
        _db.SocialChannels.FirstOrDefaultAsync(c => c.Platform == platform, ct);

    public async Task<IReadOnlyList<SocialChannel>> ListAsync(CancellationToken ct = default) =>
        await _db.SocialChannels.AsNoTracking().OrderBy(c => c.Platform).ToListAsync(ct);

    public void Update(SocialChannel channel) => _db.SocialChannels.Update(channel);
}
