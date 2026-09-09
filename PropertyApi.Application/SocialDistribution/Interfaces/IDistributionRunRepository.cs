using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

public interface IDistributionRunRepository
{
    Task AddAsync(DistributionRun run, CancellationToken ct = default);

    Task<DistributionRun?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<DistributionRun>> GetByPropertyIdAsync(Guid propertyId, int page, int pageSize, CancellationToken ct = default);

    void Update(DistributionRun run);
}
