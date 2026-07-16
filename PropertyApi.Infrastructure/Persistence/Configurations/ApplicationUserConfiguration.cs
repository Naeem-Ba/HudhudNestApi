using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration
    : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(
        EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users");

        builder.HasIndex(user => user.Email)
            .IsUnique();

        builder.HasIndex(user => user.IsDeleted);

        builder.Property(user => user.NormalizedPhoneNumber)
            .HasMaxLength(16);

        builder.HasIndex(user => user.NormalizedPhoneNumber)
            .IsUnique()
            .HasFilter("\"NormalizedPhoneNumber\" IS NOT NULL");

        builder.Property(user => user.PhoneVerificationState)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(user => user.LastPhoneVerificationNotificationKey).HasMaxLength(96);
        builder.HasIndex(user => user.PhoneVerificationDueAtUtc);
    }
}
