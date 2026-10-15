using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

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

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Domain.Services.Enums.ServiceProviderStatus.Active)
            .HasSentinel((Domain.Services.Enums.ServiceProviderStatus)(-1))
            .IsRequired();

        builder.Property(p => p.StatusReason).HasMaxLength(2000);

        // Admin-approved scope, same native-array approach as Plan.FeatureKeys.
        builder.Property(p => p.ApprovedCategories)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();
        builder.Property(p => p.CoverageGovernorateIds)
            .HasColumnType("integer[]")
            .HasDefaultValueSql("'{}'::integer[]")
            .IsRequired();

        // Two admins changing the same provider at once get a 409, not a silent overwrite.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        // One UserAccount runs at most one provider profile (see class remarks). Restrict:
        // an account with a provider profile must deactivate/transfer it first, matching the
        // Restrict convention used for every other UserAccount-owning relationship.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

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
