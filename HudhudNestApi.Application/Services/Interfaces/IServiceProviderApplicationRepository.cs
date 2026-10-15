using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Application.Services.Interfaces;

public interface IServiceProviderApplicationRepository
{
    Task AddAsync(ServiceProviderApplication application, CancellationToken ct = default);

    Task<ServiceProviderApplication?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The applicant's Pending or NeedsInfo application, if any (at most one, by index).</summary>
    Task<ServiceProviderApplication?> GetOpenByApplicantAsync(Guid applicantUserId, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ServiceProviderApplication>> ListByApplicantAsync(Guid applicantUserId, CancellationToken ct = default);

    /// <summary>Admin queue. Search matches the reference number, names, email or phone.</summary>
    Task<(IReadOnlyList<ServiceProviderApplication> Items, int Total)> SearchAsync(
        ServiceProviderApplicationFilter filter, CancellationToken ct = default);

    /// <summary>Open applications waiting on an admin (Pending only; NeedsInfo waits on the applicant).</summary>
    Task<int> CountPendingAsync(CancellationToken ct = default);
}

public sealed record ServiceProviderApplicationFilter(
    string? Search,
    ServiceProviderApplicationStatus? Status,
    ServiceCategory? Category,
    int? GovernorateId,
    int Page,
    int PageSize);
