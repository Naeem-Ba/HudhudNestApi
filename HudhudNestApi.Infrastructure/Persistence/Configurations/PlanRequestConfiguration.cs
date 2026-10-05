using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Plans.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class PlanRequestConfiguration : IEntityTypeConfiguration<PlanRequest>
{
    public void Configure(EntityTypeBuilder<PlanRequest> builder)
    {
        builder.ToTable("PlanRequests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.ResolutionNote).HasMaxLength(1000);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(r => r.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // The administrator's queue is the primary read path: Pending rows, oldest first.
        builder.HasIndex(r => new { r.Status, r.CreatedAt });

        // At most one outstanding request per user. Filtered to Pending (0) only, same
        // convention as AgencyInvitationConfiguration, so resolved rows never block a new
        // request; SelectPlanCommandHandler's per-user advisory lock is what actually
        // serializes the race this backstops.
        builder.HasIndex(r => r.UserId)
            .IsUnique()
            .HasDatabaseName("IX_PlanRequests_UserId_Pending")
            .HasFilter("\"Status\" = 0");

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
