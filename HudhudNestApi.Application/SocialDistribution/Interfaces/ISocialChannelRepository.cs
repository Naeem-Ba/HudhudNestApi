using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

public interface ISocialChannelRepository
{
    Task AddAsync(SocialChannel channel, CancellationToken ct = default);

    Task<SocialChannel?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<SocialChannel?> GetByPlatformAsync(SocialPlatform platform, CancellationToken ct = default);

    Task<IReadOnlyList<SocialChannel>> ListAsync(CancellationToken ct = default);

    void Update(SocialChannel channel);
}
