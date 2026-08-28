using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Services.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceProviderConfiguration : IEntityTypeConfiguration<ServiceProvider>
{
    public void Configure(EntityTypeBuilder<ServiceProvider> builder)
    {
        builder.ToTable("ServiceProviders");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(150);
        builder.Property(p => p.Bio).HasMaxLength(2000);
        builder.Property(p => p.LogoUrl).HasMaxLength(2048);
        builder.Property(p => p.LogoPublicId).HasMaxLength(255);
        builder.Property(p => p.ContactEmail).HasMaxLength(320);
        builder.Property(p => p.ContactPhone).HasMaxLength(50);

        builder.Property(p => p.VerificationLevel)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // One provider profile per user, live rows only — a soft-deleted provider does not
        // block that user from being onboarded again later.
        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasDatabaseName("IX_ServiceProviders_UserId")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(p => p.AgencyId);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
