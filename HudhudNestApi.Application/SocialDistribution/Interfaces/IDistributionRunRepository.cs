using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

public interface IDistributionRunRepository
{
    Task AddAsync(DistributionRun run, CancellationToken ct = default);

    Task<DistributionRun?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<DistributionRun>> GetByPropertyIdAsync(Guid propertyId, int page, int pageSize, CancellationToken ct = default);

    void Update(DistributionRun run);
}
