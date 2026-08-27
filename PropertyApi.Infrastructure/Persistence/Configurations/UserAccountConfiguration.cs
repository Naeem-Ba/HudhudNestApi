using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Plans.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class UserAccountConfiguration
    : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(
        EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");

        builder.HasKey(x => x.Id);

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
    }
}