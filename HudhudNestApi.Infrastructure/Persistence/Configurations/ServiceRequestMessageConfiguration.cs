using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceRequestMessageConfiguration : IEntityTypeConfiguration<ServiceRequestMessage>
{
    public void Configure(EntityTypeBuilder<ServiceRequestMessage> builder)
    {
        builder.ToTable("ServiceRequestMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.SenderParty).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Body).IsRequired().HasMaxLength(1000);

        builder.HasOne<ServiceRequest>()
            .WithMany()
            .HasForeignKey(m => m.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<HudhudNestApi.Domain.Users.Entities.UserAccount>()
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ServiceRequestId, m.CreatedAt });

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
