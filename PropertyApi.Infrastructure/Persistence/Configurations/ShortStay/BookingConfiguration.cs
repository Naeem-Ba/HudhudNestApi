using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("ShortStayBookings");
        builder.HasKey(b => b.Id);

        // Optimistic concurrency — same rationale as ShortStayListingConfiguration: two hosts
        // (or a host + the expiry hosted service) racing to change a booking's status now get
        // a clean DbUpdateConcurrencyException instead of a silent last-write-wins.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(b => b.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(b => b.TotalAmount).HasColumnType("decimal(18,4)");
        builder.Property(b => b.DepositAmount).HasColumnType("decimal(18,4)");
        builder.Property(b => b.RemainingAmount).HasColumnType("decimal(18,4)");

        builder.Property(b => b.HostNote).HasMaxLength(1000);
        builder.Property(b => b.CancellationReason).HasMaxLength(1000);
        builder.Property(b => b.CancellationPolicyCustomTermsText).HasMaxLength(2000);
        builder.Property(b => b.HouseRulesSnapshotText).IsRequired().HasMaxLength(2000);

        // GuestComposition — owned value object, always present (never null on Booking).
        builder.OwnsOne(b => b.Guests, guests =>
        {
            guests.Property(g => g.Adults).HasColumnName("Adults").IsRequired();
            guests.Property(g => g.Children).HasColumnName("Children").IsRequired();
            guests.Property(g => g.Infants).HasColumnName("Infants").IsRequired();
        });

        builder.HasOne(b => b.Unit)
            .WithMany()
            .HasForeignKey(b => b.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.UnitId);
        builder.HasIndex(b => b.GuestId);
        builder.HasIndex(b => b.Status);
    }
}
