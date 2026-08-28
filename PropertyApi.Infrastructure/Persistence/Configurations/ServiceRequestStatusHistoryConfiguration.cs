using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceRequestStatusHistoryConfiguration
    : IEntityTypeConfiguration<ServiceRequestStatusHistory>
{
    public void Configure(EntityTypeBuilder<ServiceRequestStatusHistory> builder)
    {
        builder.ToTable("ServiceRequestStatusHistories");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.Note).HasMaxLength(1000);

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(h => h.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => h.ServiceRequestId);

        builder.HasQueryFilter(h => !h.IsDeleted);
    }
}
