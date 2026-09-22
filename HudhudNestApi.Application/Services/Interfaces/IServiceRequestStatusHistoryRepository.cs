using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Interfaces;

public interface IServiceRequestStatusHistoryRepository
{
    Task AddAsync(ServiceRequestStatusHistory entry, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceRequestStatusHistory>> GetByServiceRequestIdAsync(
        Guid serviceRequestId, CancellationToken ct = default);
}
