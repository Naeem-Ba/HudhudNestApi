using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Interfaces;

public interface IServiceReviewDocumentRepository
{
    Task AddAsync(ServiceReviewDocument document, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceReviewDocument>> GetByServiceRequestIdAsync(
        Guid serviceRequestId, CancellationToken ct = default);
}
