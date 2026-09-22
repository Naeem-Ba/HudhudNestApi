using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Infrastructure.Identity.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration
    : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(
        EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users");

        // Case-sensitive, on the raw column -- kept for backward compatibility, but this is
        // NOT what protects against a case-variant duplicate (e.g. "user@example.com" vs
        // "USER@example.com"). That real protection is the functional unique index
        // IX_Users_Email_Lower on lower("Email"), added by raw SQL in the
        // AddEmailLowerCaseUniqueIndex migration (Finding F3,
        // docs/DATABASE-PRODUCTION-READINESS.md) -- EF Core's fluent API has no first-class way
        // to declare a Postgres expression index, so it isn't modeled here; don't let the two
        // drift apart without updating both.
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
