using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Interfaces;

public interface IServiceRequestDocumentRepository
{
    Task AddAsync(ServiceRequestDocument document, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceRequestDocument>> GetByServiceRequestIdAsync(
        Guid serviceRequestId, CancellationToken ct = default);
}
