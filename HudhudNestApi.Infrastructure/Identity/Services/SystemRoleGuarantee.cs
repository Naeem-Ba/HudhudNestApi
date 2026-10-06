using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using ApplicationRole = HudhudNestApi.Infrastructure.Identity.Entities.ApplicationRole;

namespace HudhudNestApi.Infrastructure.Identity.Services;

/// <summary>
/// Makes sure a platform role exists before an account is added to it.
///
/// <c>UserManager.AddToRoleAsync</c> does not answer "no such role" with a failed result: it
/// throws <see cref="InvalidOperationException"/> ("Role USER does not exist."), which no
/// handler maps, so every registration (email, phone, social) became an HTTP 500 whenever the
/// Roles table lacked the row.
///
/// The roles are normally created at startup by <see cref="DatabaseSeeder"/>, but that runs in
/// one transaction together with the lookup-data seeds and only logs when it fails, so a failure
/// in any earlier seed leaves the API serving requests with no roles at all. Sign-up must not
/// depend on that having worked, so the role is created here, at the point of use, when it is
/// found missing. Only the platform's own roles are ever created this way.
/// </summary>
public sealed class SystemRoleGuarantee
{
    // Two first registrations racing must not both insert the role: the loser would fail on the
    // unique index and poison its own transaction. (Its own key, not the seeder's.)
    private const long RoleCreationLockKey = 20261005201500;

    private readonly RoleManager<ApplicationRole> _roles;
    private readonly AppDbContext _db;
    private readonly ILogger<SystemRoleGuarantee> _logger;

    public SystemRoleGuarantee(
        RoleManager<ApplicationRole> roles,
        AppDbContext db,
        ILogger<SystemRoleGuarantee> logger)
    {
        _roles = roles;
        _db = db;
        _logger = logger;
    }

    public async Task EnsureExistsAsync(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        // Fast path: one indexed lookup, the only cost once the roles exist.
        if (await _roles.RoleExistsAsync(role))
            return;

        // Not ours to invent: let Identity report an unknown role as it always has.
        if (!ApplicationRolesSeed.IsSystemRole(role))
            return;

        // Slow path. The lock lasts to the end of the surrounding transaction, so a racing request
        // waits for the first to commit and then finds the role in the re-check below.
        if (_db.Database.IsNpgsql())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({RoleCreationLockKey});");
        }

        if (await ApplicationRolesSeed.EnsureAsync(_roles, role))
        {
            _logger.LogError(
                "System role {Role} was missing and has been created. Startup seeding of the " +
                "reference data and roles did not complete; search the startup log for " +
                "\"Reference-data seeding failed\" -- the lookup tables may be incomplete too.",
                role);
        }
    }
}
