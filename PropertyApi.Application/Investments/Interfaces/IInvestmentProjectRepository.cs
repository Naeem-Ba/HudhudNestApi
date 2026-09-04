using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Investments.Interfaces;

public interface IInvestmentProjectRepository
{
    /// <summary>Full entity, tracked — for command handlers only.</summary>
    Task<InvestmentProject?> GetByIdAsync(Guid id, CancellationToken ct = default);

    void Add(InvestmentProject project);

    /// <summary>Public list — Published projects only, server-side filtered/paged/projected.</summary>
    Task<PagedResult<InvestmentProjectListDto>> GetPublishedListAsync(
        InvestmentProjectFilterDto filter,
        CancellationToken ct = default);

    /// <summary>Public detail page — null unless the project is Published.</summary>
    Task<InvestmentProjectDetailsDto?> GetPublishedDetailsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lightweight card projection regardless of status — used by the owning user's own
    /// watchlist/interest lists, never exposed to a different user.</summary>
    Task<InvestmentProjectListDto?> GetListItemAsync(Guid id, CancellationToken ct = default);

    /// <summary>Admin/staff list across every lifecycle state.</summary>
    Task<PagedResult<AdminInvestmentProjectListDto>> GetAdminListAsync(
        AdminInvestmentProjectFilterDto filter,
        CancellationToken ct = default);

    /// <summary>Full detail projection regardless of status — for the admin review screen.</summary>
    Task<InvestmentProjectDetailsDto?> GetDetailsForAdminAsync(Guid id, CancellationToken ct = default);
}
