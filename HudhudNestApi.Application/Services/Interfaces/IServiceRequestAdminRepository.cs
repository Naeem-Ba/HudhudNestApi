using HudhudNestApi.Application.Services.Admin;
using HudhudNestApi.Application.Services.ProviderOnboarding;

namespace HudhudNestApi.Application.Services.Interfaces;

/// <summary>Read side of the admin "services" page: search and statistics over all requests.</summary>
public interface IServiceRequestAdminRepository
{
    Task<(IReadOnlyList<AdminServiceRequestSummaryDto> Items, int Total)> SearchAsync(
        AdminServiceRequestFilter filter, DateTime utcNow, CancellationToken ct = default);

    Task<ServiceStatsDto> GetStatsAsync(DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>Open longer than this without any update, or disputed by the customer.</summary>
    static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);
}
