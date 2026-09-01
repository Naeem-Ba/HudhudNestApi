using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Plans.Entities;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Users.Enums;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class UserAccountConfiguration
    : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(
        EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");

        builder.HasKey(x => x.Id);

        // Shared-PK 1:1 with the Identity user (Users.Id) -- UserAccount.Id is always set to
        // the owning ApplicationUser's id by whoever creates the account (see
        // ApplicationUser/UserAccount doc comments for why auth and profile are split tables).
        // Restrict, not Cascade: the pairing must never silently break by deleting one side out
        // from under the other -- the app deletes/creates both together explicitly.
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<UserAccount>(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency (RELEASE-BLOCKERS-AR.md B-9b). Same xmin/IsRowVersion mapping
        // as PropertyConfiguration -- see that file's comment for why xmin, not a dedicated
        // column. UserAccount is one row updated from several independent handlers that do not
        // coordinate with each other (UpdateUserCommandHandler, SelectPlanCommandHandler, the
        // agency invitation accept/leave flow's JoinAgency/LeaveAgency, avatar upload). Without
        // this, a stale read racing e.g. a concurrent agency-membership change on the same
        // account silently overwrites whichever field it was carrying, because EF only emits
        // an UPDATE for the columns the handler touched -- there was nothing to detect the
        // race, only to avoid colliding on the same column. This does not apply to Identity's
        // own row (security stamp, lockout, password hash): that is a separate table owned by
        // ASP.NET Identity, not UserAccount.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(x => x.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(150);

        builder.Property(x => x.TaxNumber)
            .HasMaxLength(1024);

        builder.Property(x => x.ProfileImageUrl)
            .HasMaxLength(2048);

        builder.Property(x => x.WhatsAppNumber)
            .HasMaxLength(1024);

        builder.Property(x => x.PreferredLanguage)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.PreferredCurrency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength();

        builder.HasIndex(x => x.CountryCode);

        // Agency membership. Nullable, and null for every account that exists today —
        // belonging to an agency is opt-in and adds nothing to an independent user.
        builder.HasOne<Agency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.SetNull);

        // Filtered: the vast majority of rows have no agency, and only the ones that do are
        // ever looked up this way (listing an agency's members, counting them).
        builder.HasIndex(x => x.AgencyId)
            .HasDatabaseName("IX_UserAccounts_AgencyId")
            .HasFilter("\"AgencyId\" IS NOT NULL");

        // Plan selection. Nullable — null means "has not chosen a plan yet", which is a
        // real, distinct state from "on the free plan" (see UserAccount.PlanId doc comment).
        // Restrict rather than SetNull: a Plan referenced by any account should not be
        // deletable out from under them; deactivate it (IsActive = false) instead.
        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.PlanId)
            .HasDatabaseName("IX_UserAccounts_PlanId")
            .HasFilter("\"PlanId\" IS NOT NULL");

        // Subscription lifecycle (admin dashboard: activate/extend/cancel a plan for free).
        // PlanStatus/PlanActivationSource stored as strings — same convention as
        // PropertyConfiguration's HasConversion<string> for PropertyStatus, so a future
        // member needs no data migration.
        builder.Property(x => x.PlanStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(PlanStatus.Active)
            .IsRequired();

        builder.Property(x => x.PlanActivationSource)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Restrict, not Cascade/SetNull: the admin who granted a plan is a fact about that
        // grant, not something that should silently vanish if their own account is later
        // deleted -- deletion of an admin account is expected to be exceedingly rare and,
        // were it to happen, should surface as an explicit conflict rather than quietly
        // erasing who activated a paying-adjacent benefit for another user.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.PlanGrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}