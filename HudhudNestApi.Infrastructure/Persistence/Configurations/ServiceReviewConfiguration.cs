using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceReviewConfiguration : IEntityTypeConfiguration<ServiceReview>
{
    public void Configure(EntityTypeBuilder<ServiceReview> builder)
    {
        builder.ToTable("ServiceReviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Comment).HasMaxLength(2000);
        builder.Property(r => r.Rating).IsRequired();
        builder.Property(r => r.IsHidden).IsRequired().HasDefaultValue(false);
        builder.Property(r => r.HiddenReason).HasMaxLength(300);

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(r => r.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceProvider>()
            .WithMany()
            .HasForeignKey(r => r.ServiceProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        // One review per request, live rows only.
        builder.HasIndex(r => r.ServiceRequestId)
            .IsUnique()
            .HasDatabaseName("IX_ServiceReviews_ServiceRequestId")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(r => r.ServiceProviderId);

        // The reviewer had no foreign key (DB audit 2026-10-29). ServiceProviderId repeats the
        // request's provider on purpose: provider rating aggregates read it without a join.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint("CK_ServiceReviews_Rating", "\"Rating\" BETWEEN 1 AND 5"));

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
