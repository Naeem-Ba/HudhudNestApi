using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceReviewConfiguration : IEntityTypeConfiguration<ServiceReview>
{
    public void Configure(EntityTypeBuilder<ServiceReview> builder)
    {
        builder.ToTable("ServiceReviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Comment).HasMaxLength(2000);
        builder.Property(r => r.Rating).IsRequired();

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

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
