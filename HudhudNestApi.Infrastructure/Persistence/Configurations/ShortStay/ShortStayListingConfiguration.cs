using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class ShortStayListingConfiguration : IEntityTypeConfiguration<ShortStayListing>
{
    public void Configure(EntityTypeBuilder<ShortStayListing> builder)
    {
        builder.ToTable("ShortStayListings");
        builder.HasKey(l => l.Id);

        // Optimistic concurrency — same xmin/IsRowVersion mapping as PropertyConfiguration.
        // A host editing the listing while a guest's CreateBooking reads its pricing/policy
        // snapshot concurrently now gets a clean DbUpdateConcurrencyException (-> 409) instead
        // of a silent last-write-wins.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(l => l.Title).IsRequired().HasMaxLength(150);
        builder.Property(l => l.Description).IsRequired().HasMaxLength(4000);
        builder.Property(l => l.CurrencyCode).IsRequired().HasMaxLength(3)
            .HasDefaultValue(ShortStayListing.DefaultCurrencyCode);
        builder.Property(l => l.City).HasMaxLength(100);

        // -- Administrative take-down (QA Phase 9, P9-D11) ----
        builder.Property(l => l.TakenDownReason).HasMaxLength(ShortStayListing.MaxTakeDownReasonLength);
        builder.HasIndex(l => l.TakenDownAt)
            .HasDatabaseName("IX_ShortStayListings_TakenDownAt")
            .HasFilter("\"TakenDownAt\" IS NOT NULL");

        builder.Property(l => l.CustomRulesText).HasMaxLength(2000);
        builder.Property(l => l.CancellationCustomTermsText).HasMaxLength(2000);

        builder.Property(l => l.Latitude).HasColumnType("decimal(9,6)");
        builder.Property(l => l.Longitude).HasColumnType("decimal(9,6)");
        builder.Property(l => l.CleaningFee).HasColumnType("decimal(18,4)");
        builder.Property(l => l.ExtraGuestFee).HasColumnType("decimal(18,4)");
        builder.Property(l => l.ExtraBedFee).HasColumnType("decimal(18,4)");
        builder.Property(l => l.DepositPercentage).HasColumnType("decimal(5,2)");

        builder.Property(l => l.LocationVisibility).HasConversion<string>().HasMaxLength(20);

        // PoolDetails — owned value object, null when the listing has no pool.
        builder.OwnsOne(l => l.PoolDetails, pool =>
        {
            pool.Property(p => p.Type).HasColumnName("PoolType").HasConversion<string>().HasMaxLength(20);
            pool.Property(p => p.Location).HasColumnName("PoolLocation").HasConversion<string>().HasMaxLength(20);
            pool.Property(p => p.IsSeasonal).HasColumnName("PoolIsSeasonal");
            pool.Property(p => p.IsHeated).HasColumnName("PoolIsHeated");
        });

        builder.HasOne(l => l.AccommodationType)
            .WithMany(t => t.Listings)
            .HasForeignKey(l => l.AccommodationTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // PropertyId is a loose, optional link: no navigation, because a short-stay listing is
        // its own aggregate and most hosts will never have a Property row at all. The database
        // still guarantees the id points at a real listing (DB audit 2026-10-29); SetNull keeps
        // the short-stay listing if that Property row is ever hard-deleted.
        builder.Property(l => l.PropertyId);
        builder.HasOne<Property>()
            .WithMany()
            .HasForeignKey(l => l.PropertyId)
            .OnDelete(DeleteBehavior.SetNull);

        // Owner and location ids had no foreign key (DB audit 2026-10-29). Users and lookups are
        // never hard-deleted (accounts are anonymised), so Restrict only blocks a mistake.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(l => l.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Governorate>()
            .WithMany()
            .HasForeignKey(l => l.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(l => l.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Neighborhood>()
            .WithMany()
            .HasForeignKey(l => l.NeighborhoodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.RoomTypes)
            .WithOne(rt => rt.ShortStayListing)
            .HasForeignKey(rt => rt.ShortStayListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Photos)
            .WithOne(p => p.ShortStayListing)
            .HasForeignKey(p => p.ShortStayListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.OwnerId);
        builder.HasIndex(l => l.IsPublished);
        builder.HasIndex(l => l.City);
        builder.HasIndex(l => l.AccommodationTypeId);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
