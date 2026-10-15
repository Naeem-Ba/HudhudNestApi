using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Interfaces;

public interface IServiceProviderEventRepository
{
    Task AddAsync(ServiceProviderEvent entry, CancellationToken ct = default);

    /// <summary>Oldest first. <paramref name="includeInternal"/> is false for the applicant's own view.</summary>
    Task<IReadOnlyList<ServiceProviderEvent>> ListForApplicationAsync(
        Guid applicationId, bool includeInternal, CancellationToken ct = default);

    /// <summary>Oldest first: the provider's own events plus those of every application linked to it.</summary>
    Task<IReadOnlyList<ServiceProviderEvent>> ListForProviderAsync(Guid serviceProviderId, CancellationToken ct = default);
}
