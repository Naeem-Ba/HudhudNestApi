using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Interfaces;

public interface IServiceOfferingRepository
{
    Task AddAsync(ServiceOffering offering, CancellationToken ct = default);

    Task<ServiceOffering?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// What the public may book in a category: active offerings of Active providers whose
    /// category is still admin-approved for that provider, and (with <paramref name="governorateId"/>)
    /// whose coverage includes that governorate. Includes the ServiceProvider navigation —
    /// offering cards always show who's behind it.
    /// </summary>
    Task<IReadOnlyList<ServiceOffering>> GetActiveByCategoryAsync(
        ServiceCategory category, int? governorateId = null, CancellationToken ct = default);

    /// <summary>Count of bookable offerings per category, same rules as <see cref="GetActiveByCategoryAsync"/>.</summary>
    Task<IReadOnlyDictionary<ServiceCategory, int>> CountActiveByCategoryAsync(
        int? governorateId = null, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceOffering>> GetByProviderIdAsync(
        Guid providerId, CancellationToken ct = default);
}
