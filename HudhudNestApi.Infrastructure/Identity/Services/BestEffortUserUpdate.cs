using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Identity.Services;

/// <summary>
/// For bookkeeping writes that must never break a sign-in (the last-login time, the failed-attempt counter).
///
/// <c>UserManager.UpdateAsync</c> reports a lost optimistic-concurrency race as a failed <see cref="IdentityResult"/> and
/// leaves the entity tracked as Modified. The next <c>SaveChangesAsync</c> on the same scoped context (the audit log
/// entry written a moment later) saves that entity again, hits the same stale <c>ConcurrencyStamp</c>, and throws
/// <see cref="DbUpdateConcurrencyException"/>, which the API answers as 409. Several devices signing in to one account
/// at the same moment therefore failed (load-test finding D11) although the password was right. When the update did not
/// go through, the pending change is dropped, so the sign-in continues and only the bookkeeping value is skipped.
/// </summary>
internal static class BestEffortUserUpdate
{
    public static async Task<IdentityResult> UpdateAsync(
        UserManager<ApplicationUser> users,
        AppDbContext db,
        ApplicationUser user)
    {
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            DiscardPendingChange(db, user);
        }

        return result;
    }

    public static void DiscardPendingChange(AppDbContext db, ApplicationUser user)
    {
        var entry = db.Entry(user);
        if (entry.State is EntityState.Modified or EntityState.Added)
        {
            entry.State = EntityState.Detached;
        }
    }
}
