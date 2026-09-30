using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

public interface ISocialPublicationStatusHistoryRepository
{
    Task AddAsync(SocialPublicationStatusHistory history, CancellationToken ct = default);

    Task<IReadOnlyList<SocialPublicationStatusHistory>> GetByPublicationIdAsync(Guid publicationId, CancellationToken ct = default);
}
