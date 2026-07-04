using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(150);
        builder.Property(x => x.TaxNumber).HasMaxLength(50);
        builder.Property(x => x.ProfileImageUrl).HasMaxLength(2048);
        builder.Property(x => x.WhatsAppNumber).HasMaxLength(32);
        builder.Property(x => x.PreferredLanguage).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PreferredCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.BanReason).HasMaxLength(500);
        builder.HasIndex(x => x.IsBanned);
        builder.HasIndex(x => x.CountryCode);
    }
}
