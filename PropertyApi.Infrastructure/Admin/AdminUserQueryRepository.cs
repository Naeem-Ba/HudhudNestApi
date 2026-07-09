using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Admin;

public sealed class AdminUserQueryRepository
    : IAdminUserQueryRepository
{
    private readonly AppDbContext _db;

    public AdminUserQueryRepository(
        AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AdminUserDto>>
        GetUsersWithPaginationAsync(
            int page,
            int pageSize,
            string? role,
            CancellationToken ct = default)
    {
        var query =
            _db.Users
                .AsNoTracking()
                .Where(user =>
                    !user.IsDeleted);

        if (!string.IsNullOrWhiteSpace(role))
        {
            query =
                from user in query

                join userRole
                    in _db.UserRoles.AsNoTracking()
                    on user.Id
                    equals userRole.UserId

                join identityRole
                    in _db.Roles.AsNoTracking()
                    on userRole.RoleId
                    equals identityRole.Id

                where identityRole.Name == role

                select user;
        }

        query =
            query.OrderByDescending(
                user => user.CreatedAt);

        var total =
            await query.CountAsync(ct);

        var users =
            await query
                .Skip(
                    (page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

        var userIds =
            users
                .Select(user => user.Id)
                .ToArray();

        /*
         * Business/profile data is stored in UserAccounts.
         *
         * Identity invariant:
         *
         * ApplicationUser.Id == UserAccount.Id
         */
        var profilesMap =
            await _db.UserAccounts
                .AsNoTracking()
                .Where(account =>
                    userIds.Contains(account.Id))
                .ToDictionaryAsync(
                    account => account.Id,
                    ct);

        var userRoles =
            await _db.UserRoles
                .AsNoTracking()
                .Where(userRole =>
                    userIds.Contains(
                        userRole.UserId))
                .Join(
                    _db.Roles.AsNoTracking(),

                    userRole =>
                        userRole.RoleId,

                    roleEntity =>
                        roleEntity.Id,

                    (userRole, roleEntity) =>
                        new
                        {
                            userRole.UserId,

                            RoleName =
                                roleEntity.Name
                        })
                .ToListAsync(ct);

        var rolesMap =
            userRoles
                .GroupBy(item =>
                    item.UserId)
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        group
                            .Select(item =>
                                item.RoleName)
                            .Where(roleName =>
                                !string.IsNullOrWhiteSpace(
                                    roleName))
                            .Cast<string>()
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .OrderBy(roleName =>
                                roleName)
                            .ToArray());

        var items =
            users
                .Select(user =>
                {
                    profilesMap.TryGetValue(
                        user.Id,
                        out var profile);

                    return new AdminUserDto
                    {
                        Id =
                            user.Id,

                        Email =
                            user.Email,

                        FirstName =
                            profile?.FirstName
                            ?? string.Empty,

                        LastName =
                            profile?.LastName
                            ?? string.Empty,

                        DisplayName =
                            profile?.DisplayName,

                        CreatedAt =
                            user.CreatedAt,

                        Roles =
                            rolesMap.TryGetValue(
                                user.Id,
                                out var roles)
                                ? roles
                                : Array.Empty<string>()
                    };
                })
                .ToArray();

        return new PagedResult<AdminUserDto>
        {
            Items =
                items,

            TotalCount =
                total,

            Page =
                page,

            PageSize =
                pageSize
        };
    }
}