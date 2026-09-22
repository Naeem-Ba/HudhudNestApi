using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class AgencyInvitationConfiguration : IEntityTypeConfiguration<AgencyInvitation>
{
    public void Configure(EntityTypeBuilder<AgencyInvitation> builder)
    {
        builder.ToTable("AgencyInvitations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status).IsRequired();
        builder.Property(i => i.ExpiresAt).IsRequired();

        builder.HasOne<Agency>()
            .WithMany()
            .HasForeignKey(i => i.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(i => i.InviterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(i => i.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The target user's invitation inbox is the primary read path.
        builder.HasIndex(i => i.TargetUserId);

        builder.HasIndex(i => i.AgencyId);

        // B-2 (RELEASE-BLOCKERS-AR.md): at most one live invitation per agency+user pair.
        // Filtered to Pending (0) only, same convention as AgencyConfiguration's slug/owner
        // indexes, so a declined/accepted/expired invitation never blocks a fresh one for
        // the same pair — CreateAgencyInvitationCommandHandler's advisory lock is what
        // actually serializes the race this backstops.
        builder.HasIndex(i => new { i.AgencyId, i.TargetUserId })
            .IsUnique()
            .HasDatabaseName("IX_AgencyInvitations_AgencyId_TargetUserId_Pending")
            .HasFilter("\"Status\" = 0");

        builder.HasQueryFilter(i => !i.IsDeleted);
    }
}
