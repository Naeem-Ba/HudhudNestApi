using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        // Booking.Create already enforces these; the database now refuses a bad row from any other
        // path too (DB audit 2026-10-29). RemainingAmount is derivable (Total - Deposit) and kept
        // as a frozen money snapshot; the check stops it drifting from the other two.
        builder.ToTable("ShortStayBookings", t =>
        {
            t.HasCheckConstraint("CK_ShortStayBookings_Dates", "\"CheckOut\" > \"CheckIn\"");
            t.HasCheckConstraint("CK_ShortStayBookings_Amounts",
                "\"DepositAmount\" >= 0 AND \"DepositAmount\" <= \"TotalAmount\" AND \"RemainingAmount\" = \"TotalAmount\" - \"DepositAmount\"");
        });
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

        // The guest had no foreign key (DB audit 2026-10-29); accounts are never hard-deleted.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(b => b.GuestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.UnitId);
        builder.HasIndex(b => b.GuestId);
        builder.HasIndex(b => b.Status);
    }
}
