using HudhudNestApi.Domain.Listings.Translations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ListingTranslationConfiguration : IEntityTypeConfiguration<ListingTranslation>
{
    public void Configure(EntityTypeBuilder<ListingTranslation> builder)
    {
        builder.ToTable("ListingTranslations", t =>
        {
            t.HasCheckConstraint("CK_ListingTranslations_Field", "\"Field\" IN (1, 2)");
            t.HasCheckConstraint("CK_ListingTranslations_Status", "\"Status\" IN (1, 2, 3, 4)");
            t.HasCheckConstraint("CK_ListingTranslations_TargetLanguage", "\"TargetLanguage\" IN ('ar', 'de', 'en')");
            t.HasCheckConstraint("CK_ListingTranslations_SourceHash", "char_length(\"SourceHash\") = 64");
            // A successful translation is never empty; nothing else may carry text.
            t.HasCheckConstraint(
                "CK_ListingTranslations_CompletedHasText",
                "(\"Status\" = 2 AND \"TranslatedText\" IS NOT NULL AND char_length(btrim(\"TranslatedText\")) > 0) OR (\"Status\" <> 2 AND \"TranslatedText\" IS NULL)");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Field).HasConversion<int>();
        builder.Property(t => t.Status).HasConversion<int>();
        builder.Property(t => t.TargetLanguage).IsRequired().HasMaxLength(2);
        builder.Property(t => t.SourceLanguage).HasMaxLength(8);
        builder.Property(t => t.SourceHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Provider).IsRequired().HasMaxLength(32);
        builder.Property(t => t.LastError).HasMaxLength(32);
        builder.Property(t => t.TranslatedText);

        // One row per (listing, field, language): concurrent requests contend on this key, which is what
        // makes claiming atomic across instances (INSERT ... ON CONFLICT).
        builder.HasIndex(t => new { t.PropertyId, t.Field, t.TargetLanguage }).IsUnique();

        // Translations are derived data: a permanent delete of the listing removes them in the same statement.
        builder.HasOne(t => t.Property)
            .WithMany()
            .HasForeignKey(t => t.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TranslationMonthlyUsageConfiguration : IEntityTypeConfiguration<TranslationMonthlyUsage>
{
    public void Configure(EntityTypeBuilder<TranslationMonthlyUsage> builder)
    {
        builder.ToTable("TranslationMonthlyUsages", t =>
            t.HasCheckConstraint("CK_TranslationMonthlyUsages_Characters", "\"Characters\" >= 0"));
        builder.HasKey(u => u.Month);
        builder.Property(u => u.Month).HasMaxLength(7);
    }
}
