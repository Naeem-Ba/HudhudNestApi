using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceReviewDocumentConfiguration : IEntityTypeConfiguration<ServiceReviewDocument>
{
    public void Configure(EntityTypeBuilder<ServiceReviewDocument> builder)
    {
        builder.ToTable("ServiceReviewDocuments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.FileUrl).IsRequired().HasMaxLength(2048);
        builder.Property(d => d.FilePublicId).HasMaxLength(255);
        builder.Property(d => d.FileType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(255);

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(d => d.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.ServiceRequestId);

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
