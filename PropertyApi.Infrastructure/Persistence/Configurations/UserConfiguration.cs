using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.DisplayName)
            .HasMaxLength(150);

        builder.Property(u => u.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength();

        builder.Property(u => u.PreferredLanguage)
            .HasMaxLength(10)
            .HasDefaultValue("en");

        builder.Property(u => u.PreferredCurrency)
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("EUR");

        builder.Property(u => u.TaxNumber)
            .HasMaxLength(50);

        builder.Property(u => u.ProfileImageUrl)
            .HasMaxLength(2048);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.IsDeleted);
        builder.HasIndex(u => u.CountryCode);
    }
}
