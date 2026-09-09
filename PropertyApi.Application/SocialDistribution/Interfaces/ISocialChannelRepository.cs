using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

public interface ISocialChannelRepository
{
    Task AddAsync(SocialChannel channel, CancellationToken ct = default);

    Task<SocialChannel?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<SocialChannel?> GetByPlatformAsync(SocialPlatform platform, CancellationToken ct = default);

    Task<IReadOnlyList<SocialChannel>> ListAsync(CancellationToken ct = default);

    void Update(SocialChannel channel);
}
