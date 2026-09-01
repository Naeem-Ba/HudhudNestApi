using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class AgencyConfiguration : IEntityTypeConfiguration<Agency>
{
    public void Configure(EntityTypeBuilder<Agency> builder)
    {
        builder.ToTable("Agencies");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.Slug)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.Property(a => a.LogoUrl)
            .HasMaxLength(2048);

        builder.Property(a => a.LogoPublicId)
            .HasMaxLength(255);

        builder.Property(a => a.ContactEmail)
            .HasMaxLength(320);

        builder.Property(a => a.ContactPhone)
            .HasMaxLength(50);

        builder.Property(a => a.City)
            .HasMaxLength(120);

        builder.Property(a => a.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired();

        builder.Property(a => a.LicenseNumber)
            .HasMaxLength(120);

        // Referential integrity for the owner, matching every other UserAccount-owning
        // relationship in the schema (AgencyInvitation.InviterUserId/TargetUserId, etc.).
        // Restrict: an owner must transfer ownership or deactivate the agency before their
        // account can be removed, never lose the link silently.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(a => a.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique among live agencies only. Filtered rather than plain, so a deleted
        // agency's slug is released instead of being reserved forever by a soft-deleted row
        // that no query can even see.
        builder.HasIndex(a => a.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Agencies_Slug")
            .HasFilter("\"IsDeleted\" = false");

        // One agency per owner, enforced in the database rather than only in the handler:
        // two concurrent create requests would both pass the handler's check.
        builder.HasIndex(a => a.OwnerUserId)
            .IsUnique()
            .HasDatabaseName("IX_Agencies_OwnerUserId")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(a => new { a.CountryCode, a.City })
            .HasDatabaseName("IX_Agencies_CountryCode_City");

        // Same soft-delete convention every other entity here follows, so a deleted agency
        // disappears from every read path without each query having to remember.
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
