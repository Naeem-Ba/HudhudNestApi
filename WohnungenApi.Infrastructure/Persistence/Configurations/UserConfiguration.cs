using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WohnungenApi.Domain.Users.Entities;

namespace WohnungenApi.Infrastructure.Persistence.Configurations;

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

        // ISO 3166-1 alpha-2: always 2 uppercase chars
        builder.Property(u => u.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength();

        // BCP-47 language tag: e.g. "en", "de", "ar"
        builder.Property(u => u.PreferredLanguage)
            .HasMaxLength(10)
            .HasDefaultValue("en");

        // ISO 4217 currency: always 3 uppercase chars
        builder.Property(u => u.PreferredCurrency)
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("EUR");

        // TaxNumber: mark as encrypted in a real system
        // Consider: builder.Property(u => u.TaxNumber).HasConversion(encryptConverter);
        builder.Property(u => u.TaxNumber)
            .HasMaxLength(50);

        builder.Property(u => u.ProfileImageUrl)
            .HasMaxLength(2048);  // max URL length

        // Indexes
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.IsDeleted);
        builder.HasIndex(u => u.CountryCode);
        builder.HasIndex(u => u.IsAgent);
    }
}