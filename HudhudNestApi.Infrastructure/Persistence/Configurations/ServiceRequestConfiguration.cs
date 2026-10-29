using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("ServiceRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestNumber).IsRequired().HasMaxLength(20);

        builder.Property(r => r.Category)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.RequesterNote).HasMaxLength(1000);
        builder.Property(r => r.ProviderNote).HasMaxLength(1000);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Property(r => r.CancellationReason).HasMaxLength(500);

        // Outcome and customer feedback. Enums are stored by name, like Status and Category.
        builder.Property(r => r.OutcomeReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.OutcomeNote).HasMaxLength(500);
        builder.Property(r => r.RequesterAnswer).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.RequesterReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.RequesterFeedbackNote).HasMaxLength(500);

        // The conversation stage.
        builder.Property(r => r.Intent).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ExpiredWaitingOn).HasConversion<string>().HasMaxLength(20);

        builder.Property(r => r.QuotedPrice).HasColumnType("decimal(18,2)");
        builder.Property(r => r.FinalPrice).HasColumnType("decimal(18,2)");

        builder.HasOne(r => r.Property)
            .WithMany()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Requester)
            .WithMany()
            .HasForeignKey(r => r.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ServiceProvider)
            .WithMany()
            .HasForeignKey(r => r.ServiceProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ServiceOffering)
            .WithMany()
            .HasForeignKey(r => r.ServiceOfferingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(r => r.QuotedPriceCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(r => r.FinalPriceCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Live rows only, so RequestNumber never collides with a soft-deleted request's
        // number either — same filtered-unique pattern as Agency.Slug.
        builder.HasIndex(r => r.RequestNumber)
            .IsUnique()
            .HasDatabaseName("IX_ServiceRequests_RequestNumber")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(r => r.RequesterId);
        builder.HasIndex(r => r.ServiceProviderId);
        builder.HasIndex(r => r.CreatedAt);
        builder.HasIndex(r => r.ServiceOfferingId);

        // The lifecycle sweep reads only open stages ordered by the quiet time.
        builder.HasIndex(r => new { r.Status, r.LastActivityAt });

        // Backstop for IServiceRequestRepository.HasActiveRequestAsync's duplicate check.
        builder.HasIndex(r => new { r.PropertyId, r.RequesterId, r.ServiceOfferingId, r.Status });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
