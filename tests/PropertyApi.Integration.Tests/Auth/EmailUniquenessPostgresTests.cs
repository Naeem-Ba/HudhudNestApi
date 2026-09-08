using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Persistence;
using Xunit;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// Real-Postgres regression coverage for Finding F3 (docs/DATABASE-PRODUCTION-READINESS.md):
/// email uniqueness must be enforced by a real, case-insensitive database constraint on
/// <c>Email</c> itself, not merely as a side effect of <c>UserName</c> always mirroring
/// <c>Email</c> in every current registration path. Every insert below sets a deliberately
/// *independent*, non-matching <c>UserName</c>/<c>NormalizedUserName</c> -- the exact scenario
/// the Phase 2 audit proved was previously unprotected -- so a pass here can only mean the new
/// functional index (<c>IX_Users_Email_Lower</c>, added by the
/// <see cref="PropertyApi.Infrastructure.Migrations.AddEmailLowerCaseUniqueIndex"/> migration)
/// is doing the real work, not Identity's own <c>UserNameIndex</c> incidentally catching it.
/// </summary>
[Collection("AuthPostgres")]
public sealed class EmailUniquenessPostgresTests
{
    [Theory]
    // Genuinely case-variant: only IX_Users_Email_Lower (Finding F3's fix) can reject these --
    // the pre-existing IX_Users_Email is case-sensitive and would treat both as distinct.
    [InlineData("test@example.com", "TEST@example.com", true)]
    [InlineData("test@example.com", "Test@Example.Com", true)]
    // Identical string: the pre-existing case-sensitive IX_Users_Email already rejects this on
    // its own, so Postgres may report either constraint -- still asserted as a regression guard,
    // just without pinning the specific index name.
    [InlineData("test@example.com", "test@example.com", false)]
    public async Task SecondInsert_WithCaseVariantEmail_IsRejected_EvenWithIndependentUserName(
        string firstEmail,
        string secondEmailCasing,
        bool requireLowerCaseIndexByName)
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            db.Users.Add(BuildUser(firstEmail, userName: "first-account-username"));
            await db.SaveChangesAsync();

            db.Users.Add(BuildUser(secondEmailCasing, userName: "a-completely-different-username"));

            // DbUpdateException wraps Npgsql's 23505 unique_violation -- this is the exact
            // scenario Phase 2 proved was previously unprotected (independent UserName means
            // Identity's own UserNameIndex cannot be what rejects this). If this ever stops
            // throwing, both unique indexes on Email have been dropped or bypassed.
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());

            if (requireLowerCaseIndexByName)
            {
                Assert.Contains(
                    "IX_Users_Email_Lower",
                    exception.InnerException?.Message ?? exception.Message,
                    StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public async Task DifferentEmails_WithIndependentUserNames_BothSucceed()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            db.Users.Add(BuildUser("first@example.com", userName: "username-one"));
            db.Users.Add(BuildUser("second@example.com", userName: "username-two"));

            // No collision: genuinely different addresses must never be rejected by the new
            // index -- this guards against an overly broad expression accidentally matching.
            await db.SaveChangesAsync();

            var count = await db.Users.CountAsync(u =>
                u.Email == "first@example.com" || u.Email == "second@example.com");
            Assert.Equal(2, count);
        });
    }

    [Fact]
    public async Task NullEmail_DoesNotCollideWithAnotherNullEmail()
    {
        // Phone-only accounts have no email at all -- the functional index must preserve
        // Postgres's standard "multiple NULLs never collide in a UNIQUE index" behavior,
        // exactly like the pre-existing IX_Users_Email already does.
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();

        await factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var first = BuildUser(email: null, userName: "phone-only-one");
            var second = BuildUser(email: null, userName: "phone-only-two");
            db.Users.Add(first);
            db.Users.Add(second);

            await db.SaveChangesAsync();

            Assert.Equal(2, await db.Users.CountAsync(u => u.Email == null));
        });
    }

    private static ApplicationUser BuildUser(string? email, string userName) => new()
    {
        Id = Guid.NewGuid(),
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email?.ToUpperInvariant(),
        EmailConfirmed = false,
        PasswordHash = "not-a-real-hash",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
}
