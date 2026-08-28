using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Application.Services.Interfaces;

public interface IServiceProviderRepository
{
    Task AddAsync(ServiceProvider provider, CancellationToken ct = default);

    Task<ServiceProvider?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>One provider profile per UserAccount — used both to load "my provider profile"
    /// and to reject a second profile for the same user.</summary>
    Task<ServiceProvider?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
}
