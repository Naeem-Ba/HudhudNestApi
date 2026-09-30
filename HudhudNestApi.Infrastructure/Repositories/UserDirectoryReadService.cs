using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Models;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

/// <summary>
/// EF Core read adapter for the user directory.
///
/// Reads Identity state, UserAccount profile state,
/// and roles without exposing persistence or Identity
/// entities to Application handlers.
///
/// Uses a bounded number of queries and avoids N+1
/// role lookups.
/// </summary>
public sealed class UserDirectoryReadService
    : IUserDirectoryReadService
{
    private readonly AppDbContext _db;

    public UserDirectoryReadService(
        AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserDirectoryEntry?>
    GetActiveByIdAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var row =
            await (
                from user in
                    _db.Users.AsNoTracking()

                where
                    user.Id == userId &&
                    !user.IsDeleted

                join account in
                    _db.Set<UserAccount>()
                        .AsNoTracking()
                    on user.Id equals account.Id
                    into accountJoin

                from account in
                    accountJoin.DefaultIfEmpty()

                select new
                {
                    UserId = user.Id,

                    DisplayName =
                        account == null
                            ? null
                            : account.DisplayName,

                    FirstName =
                        account == null
                            ? null
                            : account.FirstName,

                    LastName =
                        account == null
                            ? null
                            : account.LastName,

                    ProfileImageUrl =
                        account == null
                            ? null
                            : account.ProfileImageUrl
                }
            )
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var displayName =
            BuildDisplayName(
                row.DisplayName,
                row.FirstName,
                row.LastName);

        return new UserDirectoryEntry(
            row.UserId,
            displayName,
            row.ProfileImageUrl);
    }

    private static string BuildDisplayName(
    string? displayName,
    string? firstName,
    string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(
                displayName))
        {
            return displayName;
        }

        var fullName =
            $"{firstName} {lastName}"
                .Trim();

        return string.IsNullOrWhiteSpace(
            fullName)
                ? "مستخدم"
                : fullName;
    }
    public async Task<IReadOnlyList<UserSummaryDto>>
        GetAllActiveAsync(
            CancellationToken ct = default)
    {
        /*
         * Query 1:
         *
         * Load active Identity users together with their
         * optional UserAccount profile projection.
         */
        var users =
            await (
                from user in
                    _db.Users.AsNoTracking()

                where !user.IsDeleted

                join account in
                    _db.Set<UserAccount>()
                        .AsNoTracking()
                    on user.Id equals account.Id
                    into accountJoin

                from account in
                    accountJoin.DefaultIfEmpty()

                orderby user.CreatedAt descending

                select new DirectoryRow(
                    user.Id,

                    account == null
                        ? null
                        : account.DisplayName,

                    account == null
                        ? null
                        : account.FirstName,

                    account == null
                        ? null
                        : account.LastName,

                    account == null
                        ? null
                        : account.ProfileImageUrl,

                    user.PhoneNumber)
            )
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            return Array.Empty<UserSummaryDto>();
        }

        var userIds =
            users
                .Select(user => user.Id)
                .ToArray();

        /*
         * Query 2:
         *
         * Load all role assignments for the complete user set
         * in one database round-trip.
         */
        var roleRows =
            await (
                from userRole in
                    _db.UserRoles.AsNoTracking()

                join role in
                    _db.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id

                where userIds.Contains(
                    userRole.UserId)

                select new
                {
                    userRole.UserId,
                    RoleName = role.Name
                }
            )
            .ToListAsync(ct);

        var rolesByUserId =
            roleRows
                .Where(
                    row =>
                        !string.IsNullOrWhiteSpace(
                            row.RoleName))
                .GroupBy(
                    row => row.UserId)
                .ToDictionary(
                    group => group.Key,

                    group =>
                        (IReadOnlyList<string>)group
                            .Select(
                                row => row.RoleName!)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList()
                            .AsReadOnly());

        var result =
            new List<UserSummaryDto>(
                users.Count);

        foreach (var user in users)
        {
            var roles =
                rolesByUserId.TryGetValue(
                    user.Id,
                    out var assignedRoles)
                    ? assignedRoles
                    : Array.Empty<string>();

            result.Add(
                new UserSummaryDto
                {
                    Id =
                        user.Id,

                    DisplayName =
                        BuildDisplayName(
                            user),

                    ProfileImageUrl =
                        user.ProfileImageUrl,

                    /*
                     * Preserve the previous GetAllUsers behavior:
                     * this administrative directory query returns
                     * the stored phone number.
                     */
                    PhoneNumber =
                        user.PhoneNumber,

                    Roles =
                        roles
                });
        }

        return result.AsReadOnly();
    }

    private static string BuildDisplayName(
        DirectoryRow user)
    {
        if (!string.IsNullOrWhiteSpace(
                user.DisplayName))
        {
            return user.DisplayName;
        }

        var fullName =
            $"{user.FirstName} {user.LastName}"
                .Trim();

        return string.IsNullOrWhiteSpace(
            fullName)
                ? "User"
                : fullName;
    }

    private sealed record DirectoryRow(
        Guid Id,
        string? DisplayName,
        string? FirstName,
        string? LastName,
        string? ProfileImageUrl,
        string? PhoneNumber);
}