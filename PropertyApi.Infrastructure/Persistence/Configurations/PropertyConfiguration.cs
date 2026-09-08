using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        // Finding F4 (docs/DATABASE-PRODUCTION-READINESS.md): each column stays nullable --
        // Area is legitimately optional except for Land listings
        // (CreatePropertyCommandValidator.cs), and the three price columns are legitimately
        // null depending on ListingType -- so every constraint is "no NULL" read as "no
        // zero/negative placeholder", not literal column nullability. Declared here (not only
        // in the AddPropertyNumericConstraints migration's raw SQL) so the EF Core model and
        // the applied schema can never silently drift apart, matching the same pattern
        // PlanConfiguration.cs already uses for CK_Plans_ListingLimit_PositiveOrUnlimited.
        builder.ToTable("Properties", t =>
        {
            t.HasCheckConstraint("CK_Properties_Area_PositiveOrNull", "\"Area\" IS NULL OR \"Area\" > 0");
            t.HasCheckConstraint("CK_Properties_Rooms_PositiveOrNull", "\"Rooms\" IS NULL OR \"Rooms\" > 0");
            t.HasCheckConstraint("CK_Properties_ColdRent_PositiveOrNull", "\"ColdRent\" IS NULL OR \"ColdRent\" > 0");
            t.HasCheckConstraint("CK_Properties_WarmRent_PositiveOrNull", "\"WarmRent\" IS NULL OR \"WarmRent\" > 0");
            t.HasCheckConstraint("CK_Properties_PurchasePrice_PositiveOrNull", "\"PurchasePrice\" IS NULL OR \"PurchasePrice\" > 0");
        });

        builder.HasKey(p => p.Id);

        // Optimistic concurrency (RELEASE-BLOCKERS-AR.md B-9). Maps the Postgres system
        // column `xmin`, which every table already has — no new column, no data migration.
        // Two concurrent writers to the same row (an owner editing while B-6's advisory-lock
        // background sweep touches the same listing, or two browser tabs) now get one
        // winner and one DbUpdateConcurrencyException instead of a silent last-write-wins.
        // Scoped to Property only for now: it is the entity B-6's background jobs mutate
        // unattended; Agency can gain the same mapping later with no schema change.
        //
        // NpgsqlEntityTypeBuilderExtensions.UseXminAsConcurrencyToken() is obsolete in this
        // Npgsql version — this is its documented replacement.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        // -- Core ---------------------------------------------
        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(200);   // Consistent with domain validation

        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(5000);

        // -- Address ------------------------------------------
        builder.Property(p => p.Street)
            .HasMaxLength(300);

        builder.Property(p => p.City)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(p => p.Region)
            .HasMaxLength(150);

        // ISO 3166-1 alpha-2: fixed 2-char code
        builder.Property(p => p.CountryCode)
            .IsRequired()
            .HasMaxLength(2)
            .IsFixedLength()
            .HasDefaultValue("SY");

        builder.Property(p => p.PostalCode)
            .HasMaxLength(20);

        // Manual fallbacks for governorates/districts with no seeded
        // Districts/Neighborhoods rows — see Property.DistrictText's and
        // Property.NeighborhoodText's doc comments.
        builder.Property(p => p.DistrictText)
            .HasMaxLength(150);

        builder.Property(p => p.NeighborhoodText)
            .HasMaxLength(150);

        // -- Geo: decimal(9,6) gives precision to ~0.11m ------
        builder.Property(p => p.Latitude)
            .HasColumnType("decimal(9,6)");

        builder.Property(p => p.Longitude)
            .HasColumnType("decimal(9,6)");

        // -- Pricing ------------------------------------------
        // decimal(18,4) supports global currencies (JPY, SYP have no cents)
        builder.Property(p => p.ColdRent)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.WarmRent)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.PurchasePrice)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.AdditionalCosts)
            .HasColumnType("decimal(18,4)");

        builder.Property(p => p.Deposit)
            .HasColumnType("decimal(18,4)");

        // ISO 4217: fixed 3-char code
        builder.Property(p => p.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("SYP");

        // -- Physical -----------------------------------------
        builder.Property(p => p.Area)
            .HasColumnType("decimal(10,2)");

        // Area's unit. Default SquareMeter backfills every existing row to what
        // Area has always meant before this column existed — fully backward
        // compatible, no data migration needed beyond the column default.
        builder.Property(p => p.AreaUnit)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(AreaUnit.SquareMeter);

        // -- تفاصيل الإيجار (Rent فقط) --------------------------
        builder.Property(p => p.RentalStartDate);
        builder.Property(p => p.RentalEndDate);

        // Nullable — a Sale listing has no rental duration, and "no value" must
        // mean exactly that rather than defaulting to a misleading duration.
        builder.Property(p => p.RentalDurationType)
            .HasConversion<string>()
            .HasMaxLength(20);

        // -- Enums: store as string for readability in DB ------
        // Changing enum order never corrupts data
        builder.Property(p => p.ListingType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(PropertyStatus.Available);

        builder.Property(p => p.Condition)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.EnergyEfficiency)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.HeatingType)
            .HasConversion<string>()
            .HasMaxLength(20);

        // -- Relationships -------------------------------------
        builder.HasOne(p => p.Owner)
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);  // Don't cascade-delete listings on user delete

        builder.HasMany(p => p.Images)
            .WithOne(i => i.Property)
            .HasForeignKey(i => i.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Messages)
            .WithOne(m => m.Property)
            .HasForeignKey(m => m.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Favorites)
            .WithOne(f => f.Property)
            .HasForeignKey(f => f.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        // -- Indexes ------------------------------------------
        // Most common search patterns:
        builder.HasIndex(p => p.CountryCode);
        builder.HasIndex(p => p.City);
        builder.HasIndex(p => new { p.CountryCode, p.City });
        builder.HasIndex(p => p.OwnerId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ListingType);
        builder.HasIndex(p => p.IsPublished);
        builder.HasIndex(p => p.IsDeleted);
        builder.HasIndex(p => p.CreatedAt);

        // ── الوضع القانوني ─────────────────────────────────────────────
        builder.Property(p => p.LegalStatus)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.ZoningStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.LegalStatusNotes).HasMaxLength(1000);
        builder.Property(p => p.ZoningNotes).HasMaxLength(500);
        builder.Property(p => p.LegalDisputeNotes).HasMaxLength(1000);

        // ── التفاصيل الإضافية ──────────────────────────────────────────
        builder.Property(p => p.NearestLandmark).HasMaxLength(300);
        builder.Property(p => p.BuildingNumber).HasMaxLength(50);
        builder.Property(p => p.GoogleMapsUrl).HasMaxLength(1000);
        builder.Property(p => p.WaterSource).HasMaxLength(50);
        builder.Property(p => p.InternetType).HasMaxLength(50);
        builder.Property(p => p.ViewDescription).HasMaxLength(300);

        builder.Property(p => p.FurnishingStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(FurnishingStatus.Unfurnished);

        // ── العلاقات الجديدة ────────────────────────────────────────────
        builder.HasOne(p => p.Governorate)
            .WithMany(g => g.Properties)
            .HasForeignKey(p => p.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.District)
            .WithMany(d => d.Properties)
            .HasForeignKey(p => p.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Neighborhood)
            .WithMany(n => n.Properties)
            .HasForeignKey(p => p.NeighborhoodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PropertyType)
            .WithMany(pt => pt.Properties)
            .HasForeignKey(p => p.PropertyTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Agent)
            .WithMany()
            .HasForeignKey(p => p.AgentId)
            .OnDelete(DeleteBehavior.SetNull);
        // ── الفهارس الجديدة ────────────────────────────────────────────
        builder.HasIndex(p => p.GovernorateId);
        builder.HasIndex(p => p.PropertyTypeId);
        builder.HasIndex(p => p.LegalStatus);
        builder.HasIndex(p => p.FurnishingStatus);
        builder.HasIndex(p => p.IsFeatured);
        builder.HasIndex(p => p.IsVerified);

        // The featured sweep looks for listings still flagged featured whose paid window has
        // already elapsed. Filtered to the flagged rows, which are a small minority of the
        // table — an unfiltered index here would be almost entirely `false`.
        builder.HasIndex(p => p.FeaturedUntil)
            .HasDatabaseName("IX_Properties_FeaturedUntil_Active")
            .HasFilter("\"IsFeatured\" = true");

        // Agency attribution. Nullable and null for every listing that exists today.
        builder.HasOne<Agency>()
            .WithMany()
            .HasForeignKey(p => p.AgencyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.AgencyId)
            .HasDatabaseName("IX_Properties_AgencyId")
            .HasFilter("\"AgencyId\" IS NOT NULL");

        // Composite index للبحث في السوق السوري
        builder.HasIndex(p => new { p.GovernorateId, p.PropertyTypeId, p.Status, p.IsPublished })
            .HasDatabaseName("IX_Properties_Syrian_Search");

        // Composite index for the most common filtered search
        builder.HasIndex(p => new { p.CountryCode, p.City, p.Status, p.IsPublished, p.IsDeleted })
            .HasDatabaseName("IX_Properties_Search");

        // Phase-0 "freshness" feature — supports a future background job that finds
        // published listings not confirmed available in the last N days.
        builder.HasIndex(p => p.LastConfirmedAvailableAt);

        // Drives all three phases of ListingExpiryHostedService, each of which filters on
        // Status plus an ExpiresAt range. Status leads because it is the more selective of
        // the two once the bulk of rows settle into Available/Expired, and because the
        // delete phase filters Status equality before the date range.
        builder.HasIndex(p => new { p.Status, p.ExpiresAt })
            .HasDatabaseName("IX_Properties_Status_ExpiresAt");

        // The warning phase looks for listings that have no warning stamp yet. A filtered
        // index keeps this to the rows that can still match instead of the whole table —
        // every listing already warned drops out of the index entirely.
        builder.HasIndex(p => p.ExpiresAt)
            .HasDatabaseName("IX_Properties_ExpiresAt_PendingWarning")
            .HasFilter("\"ExpiryWarningSentAt\" IS NULL");
    }
}
