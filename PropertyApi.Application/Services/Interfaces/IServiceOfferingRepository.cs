using PropertyApi.Domain.Services.Entities;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Application.Services.Interfaces;

public interface IServiceOfferingRepository
{
    Task AddAsync(ServiceOffering offering, CancellationToken ct = default);

    Task<ServiceOffering?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Includes the ServiceProvider navigation — offering cards always show who's behind it.</summary>
    Task<IReadOnlyList<ServiceOffering>> GetActiveByCategoryAsync(
        ServiceCategory category, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceOffering>> GetByProviderIdAsync(
        Guid providerId, CancellationToken ct = default);
}
