using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Interfaces;

public interface IServiceProviderRepository
{
    Task AddAsync(ServiceProvider provider, CancellationToken ct = default);

    Task<ServiceProvider?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>One provider profile per UserAccount — used both to load "my provider profile"
    /// and to reject a second profile for the same user.</summary>
    Task<ServiceProvider?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Admin directory. Search matches the display name, contact email or phone.</summary>
    Task<(IReadOnlyList<ServiceProvider> Items, int Total)> SearchAsync(
        ServiceProviderFilter filter, CancellationToken ct = default);
}

public sealed record ServiceProviderFilter(
    string? Search,
    ServiceProviderStatus? Status,
    ServiceCategory? Category,
    int? GovernorateId,
    int Page,
    int PageSize);
