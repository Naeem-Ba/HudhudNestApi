using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.SocialDistribution;

public sealed class SocialAccountConfiguration : IEntityTypeConfiguration<SocialAccount>
{
    public void Configure(EntityTypeBuilder<SocialAccount> builder)
    {
        builder.ToTable("SocialAccounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Platform).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.DisplayName).IsRequired().HasMaxLength(150);
        builder.Property(a => a.ExternalAccountId).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Status).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.AccountType).IsRequired().HasConversion<string>().HasMaxLength(30);

        // CredentialReference is intentionally NOT configured here — it is encrypted at rest via
        // the ASP.NET Core Data-Protection column converter, which needs the DbContext's own
        // IDataProtectionProvider instance and is therefore wired directly in
        // AppDbContext.ConfigureEncryptedSocialAccountFields, the same place
        // UserAccount.WhatsAppNumber/TaxNumber are configured.

        // Restrict: an account's publication history must survive its channel being deprecated.
        builder.HasOne<SocialChannel>().WithMany().HasForeignKey(a => a.SocialChannelId).OnDelete(DeleteBehavior.Restrict);

        // No navigation property added to the Governorate lookup entity (keeps this bounded
        // context from reaching into Lookups) — FK-only, same pattern as PropertyShareEvent's
        // relationship to Property.
        builder.HasOne<Governorate>().WithMany().HasForeignKey(a => a.GovernorateId).OnDelete(DeleteBehavior.Restrict);

        // Spec §6.2: ExternalAccountId must be unique per platform (not globally — two different
        // platforms could coincidentally reuse the same external id shape).
        builder.HasIndex(a => new { a.Platform, a.ExternalAccountId }).IsUnique();

        builder.HasIndex(a => a.GovernorateId);
        builder.HasIndex(a => a.Status);
    }
}
