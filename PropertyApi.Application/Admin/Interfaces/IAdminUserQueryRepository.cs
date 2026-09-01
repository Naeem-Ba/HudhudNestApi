using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Admin.Interfaces;

public interface IAdminUserQueryRepository
{
    /// <summary>
    /// <paramref name="search"/> matches (case-insensitive, partial) against name, email,
    /// phone number, or a full/partial user id — whichever the admin typed.
    /// <paramref name="planTier"/> and <paramref name="accountStatus"/> filter on the
    /// computed values AdminUserDto exposes (PlanTier / AccountStatus), not raw columns.
    /// </summary>
    Task<PagedResult<AdminUserDto>> GetUsersWithPaginationAsync(
        int page,
        int pageSize,
        string? role,
        string? search = null,
        string? planTier = null,
        string? accountStatus = null,
        CancellationToken ct = default);

    Task<AdminUserDetailDto?> GetUserDetailAsync(
        Guid userId,
        CancellationToken ct = default);
}

