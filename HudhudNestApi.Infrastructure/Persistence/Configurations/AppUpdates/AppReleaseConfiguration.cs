using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.AppUpdates.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.AppUpdates;

public sealed class AppReleaseConfiguration : IEntityTypeConfiguration<AppRelease>
{
    public void Configure(EntityTypeBuilder<AppRelease> builder)
    {
        builder.ToTable("AppReleases");
        builder.HasKey(r => r.Id);

        // Optimistic concurrency — same xmin pattern as InvestmentProject/Property.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(r => r.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Version).IsRequired().HasMaxLength(20);
        builder.Property(r => r.MinimumSupportedVersion).IsRequired().HasMaxLength(20);
        builder.Property(r => r.StoreUrl).HasMaxLength(2048);
        builder.Property(r => r.ReleaseNotesAr).HasMaxLength(4000);
        builder.Property(r => r.ReleaseNotesEn).HasMaxLength(4000);
        builder.Property(r => r.ReleaseNotesDe).HasMaxLength(4000);
        builder.Property(r => r.ReleaseDate).HasColumnType("timestamp without time zone");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(r => r.DeletedAt).HasColumnType("timestamp without time zone");

        // Prevents two ENABLED releases for the same platform+version. A partial index (rather
        // than an unconditional unique index) so a disabled duplicate or a soft-deleted row
        // never blocks creating/re-enabling a corrected one for the same platform+version.
        builder.HasIndex(r => new { r.Platform, r.Version })
            .IsUnique()
            .HasFilter("\"IsEnabled\" = true AND \"IsDeleted\" = false");

        // Hot lookup path: WHERE Platform = ? AND IsEnabled = ? AND IsDeleted = ? — hit on
        // every app launch via GetEffectiveReleaseAsync.
        builder.HasIndex(r => new { r.Platform, r.IsEnabled });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
