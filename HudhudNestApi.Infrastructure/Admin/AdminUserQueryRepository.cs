using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Admin;

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
            string? search = null,
            string? planTier = null,
            string? accountStatus = null,
            CancellationToken ct = default)
    {
        // Admin dashboard: unlike the original role-only list, disabled accounts stay
        // visible here (with AccountStatus="Disabled") rather than vanishing entirely —
        // an admin reviewing/managing accounts needs to find a disabled one, not just the
        // active ones. accountStatus lets them filter either way.
        var query = _db.Users.AsNoTracking().AsQueryable();

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

        if (!string.IsNullOrWhiteSpace(accountStatus))
        {
            var wantsDisabled = string.Equals(accountStatus, "Disabled", StringComparison.OrdinalIgnoreCase);
            query = query.Where(user => user.IsDeleted == wantsDisabled);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            if (Guid.TryParse(term, out var searchId))
            {
                query = query.Where(user => user.Id == searchId);
            }
            else
            {
                var likeTerm = $"%{term}%";

                query = query.Where(user =>
                    EF.Functions.ILike(user.Email ?? string.Empty, likeTerm)
                    || EF.Functions.ILike(user.PhoneNumber ?? string.Empty, likeTerm)
                    || _db.UserAccounts.Any(account =>
                        account.Id == user.Id
                        && (EF.Functions.ILike(account.FirstName, likeTerm)
                            || EF.Functions.ILike(account.LastName, likeTerm)
                            || EF.Functions.ILike(account.DisplayName ?? string.Empty, likeTerm))));
            }
        }

        if (!string.IsNullOrWhiteSpace(planTier))
        {
            query = query.Where(user =>
                _db.UserAccounts.Any(account =>
                    account.Id == user.Id
                    && account.PlanId != null
                    && _db.Plans.Any(plan => plan.Id == account.PlanId && plan.Tier == planTier)));
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

        var planIds =
            profilesMap.Values
                .Where(account => account.PlanId is not null)
                .Select(account => account.PlanId!.Value)
                .Distinct()
                .ToArray();

        var plansMap =
            await _db.Plans
                .AsNoTracking()
                .Where(plan => planIds.Contains(plan.Id))
                .ToDictionaryAsync(plan => plan.Id, ct);

        var now = DateTime.UtcNow;

        var listingCounts =
            await _db.Properties
                .AsNoTracking()
                .Where(p => userIds.Contains(p.OwnerId))
                .GroupBy(p => p.OwnerId)
                .Select(g => new
                {
                    OwnerId = g.Key,
                    Total = g.Count(),
                    Active = g.Count(p => p.Status != PropertyStatus.Expired),
                    Featured = g.Count(p => p.IsFeatured && p.FeaturedUntil != null && p.FeaturedUntil > now)
                })
                .ToDictionaryAsync(x => x.OwnerId, ct);

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
                    profilesMap.TryGetValue(user.Id, out var profile);
                    plansMap.TryGetValue(
                        profile?.PlanId ?? Guid.Empty,
                        out var plan);
                    listingCounts.TryGetValue(user.Id, out var counts);

                    return new AdminUserDto
                    {
                        Id = user.Id,
                        Email = user.Email,
                        PhoneNumber = user.PhoneNumber,
                        FirstName = profile?.FirstName ?? string.Empty,
                        LastName = profile?.LastName ?? string.Empty,
                        DisplayName = profile?.DisplayName,
                        CreatedAt = user.CreatedAt,
                        AccountStatus = user.IsDeleted ? "Disabled" : "Active",
                        PlanTier = plan?.Tier,
                        PlanStatus = (profile?.GetEffectivePlanStatus(now) ?? Domain.Users.Enums.EffectivePlanStatus.NoPlan).ToString(),
                        PlanExpiresAt = profile?.PlanExpiresAt,
                        TotalListings = counts?.Total ?? 0,
                        ActiveListings = counts?.Active ?? 0,
                        FeaturedListings = counts?.Featured ?? 0,
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

    public async Task<AdminUserDetailDto?> GetUserDetailAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return null;
        }

        var profile = await _db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == userId, ct);

        var plan = profile?.PlanId is { } planId
            ? await _db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
            : null;

        var roles = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                _db.Roles.AsNoTracking(),
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => r.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        var listingCounts = await _db.Properties.AsNoTracking()
            .Where(p => p.OwnerId == userId)
            .GroupBy(p => p.OwnerId)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(p => p.Status != PropertyStatus.Expired),
                Featured = g.Count(p => p.IsFeatured && p.FeaturedUntil != null && p.FeaturedUntil > now)
            })
            .FirstOrDefaultAsync(ct);

        return new AdminUserDetailDto
        {
            Id = user.Id,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            FirstName = profile?.FirstName ?? string.Empty,
            LastName = profile?.LastName ?? string.Empty,
            DisplayName = profile?.DisplayName,
            CreatedAt = user.CreatedAt,
            AccountStatus = user.IsDeleted ? "Disabled" : "Active",
            Roles = roles!.Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToArray(),

            PlanTier = plan?.Tier,
            PlanNameKey = plan?.NameKey,
            PlanStatus = (profile?.GetEffectivePlanStatus(now) ?? Domain.Users.Enums.EffectivePlanStatus.NoPlan).ToString(),
            PlanStartedAt = profile?.PlanSelectedAt,
            PlanExpiresAt = profile?.PlanExpiresAt,
            PlanCancelledAt = profile?.PlanCancelledAt,
            PlanActivationSource = profile?.PlanActivationSource?.ToString(),
            ListingLimit = plan?.ListingLimit,

            TotalListings = listingCounts?.Total ?? 0,
            ActiveListings = listingCounts?.Active ?? 0,
            FeaturedListings = listingCounts?.Featured ?? 0
        };
    }
}
