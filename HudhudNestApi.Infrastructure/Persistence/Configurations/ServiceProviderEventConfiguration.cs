using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceProviderEventConfiguration : IEntityTypeConfiguration<ServiceProviderEvent>
{
    public void Configure(EntityTypeBuilder<ServiceProviderEvent> builder)
    {
        builder.ToTable("ServiceProviderEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(ServiceProviderApplication.MaxMessageLength);

        builder.HasOne<ServiceProviderApplication>()
            .WithMany()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceProvider>()
            .WithMany()
            .HasForeignKey(e => e.ServiceProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.ApplicationId, e.CreatedAt });
        builder.HasIndex(e => new { e.ServiceProviderId, e.CreatedAt });

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
