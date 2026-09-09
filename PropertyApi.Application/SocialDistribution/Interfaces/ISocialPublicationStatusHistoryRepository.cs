using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

public interface ISocialPublicationStatusHistoryRepository
{
    Task AddAsync(SocialPublicationStatusHistory history, CancellationToken ct = default);

    Task<IReadOnlyList<SocialPublicationStatusHistory>> GetByPublicationIdAsync(Guid publicationId, CancellationToken ct = default);
}
