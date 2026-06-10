using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Admin.Interfaces;

public interface IAdminUserQueryRepository
{
    Task<PagedResult<AdminUserDto>> GetUsersWithPaginationAsync(
        int page,
        int pageSize,
        string? role,
        CancellationToken ct = default);
}
