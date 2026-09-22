using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

public interface ISocialPublicationDeadLetterRepository
{
    Task AddAsync(SocialPublicationDeadLetter deadLetter, CancellationToken ct = default);

    Task<SocialPublicationDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Used to avoid recording a second dead letter for a publication that already has an unresolved one (spec §13 dedupe on Requeue/re-failure).</summary>
    Task<bool> ExistsUnresolvedForPublicationAsync(Guid publicationId, CancellationToken ct = default);

    Task<PagedResult<SocialPublicationDeadLetter>> GetPagedAsync(bool? resolved, int page, int pageSize, CancellationToken ct = default);

    void Update(SocialPublicationDeadLetter deadLetter);
}
