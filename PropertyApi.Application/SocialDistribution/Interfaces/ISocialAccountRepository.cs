using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

public interface ISocialAccountRepository
{
    Task AddAsync(SocialAccount account, CancellationToken ct = default);

    Task<SocialAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Enforces the spec's per-platform ExternalAccountId uniqueness rule (§6.2).</summary>
    Task<bool> ExternalAccountExistsAsync(SocialPlatform platform, string externalAccountId, CancellationToken ct = default);

    Task<PagedResult<SocialAccount>> GetPagedAsync(SocialAccountFilterDto filter, CancellationToken ct = default);

    void Update(SocialAccount account);
}
