using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

public interface ISocialAccountRepository
{
    Task AddAsync(SocialAccount account, CancellationToken ct = default);

    Task<SocialAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Enforces the spec's per-platform ExternalAccountId uniqueness rule (§6.2).</summary>
    Task<bool> ExternalAccountExistsAsync(SocialPlatform platform, string externalAccountId, CancellationToken ct = default);

    Task<PagedResult<SocialAccount>> GetPagedAsync(SocialAccountFilterDto filter, CancellationToken ct = default);

    void Update(SocialAccount account);
}
