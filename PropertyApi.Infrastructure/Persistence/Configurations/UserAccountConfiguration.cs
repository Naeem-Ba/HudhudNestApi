using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class UserAccountConfiguration
    : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(
        EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(150);

        builder.Property(x => x.TaxNumber)
            .HasMaxLength(1024);

        builder.Property(x => x.ProfileImageUrl)
            .HasMaxLength(2048);

        builder.Property(x => x.WhatsAppNumber)
            .HasMaxLength(1024);

        builder.Property(x => x.PreferredLanguage)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.PreferredCurrency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(x => x.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength();

        builder.HasIndex(x => x.CountryCode);

        // Agency membership. Nullable, and null for every account that exists today —
        // belonging to an agency is opt-in and adds nothing to an independent user.
        builder.HasOne<Agency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.SetNull);

        // Filtered: the vast majority of rows have no agency, and only the ones that do are
        // ever looked up this way (listing an agency's members, counting them).
        builder.HasIndex(x => x.AgencyId)
            .HasDatabaseName("IX_UserAccounts_AgencyId")
            .HasFilter("\"AgencyId\" IS NOT NULL");
    }
}