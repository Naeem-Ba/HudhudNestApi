using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("Leads");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.FullName).IsRequired().HasMaxLength(150);
        builder.Property(l => l.Phone).IsRequired().HasMaxLength(30);
        builder.Property(l => l.City).IsRequired().HasMaxLength(100);
        builder.Property(l => l.UserType).IsRequired().HasMaxLength(30);
        builder.Property(l => l.Notes).HasMaxLength(1000);
        builder.Property(l => l.Source).IsRequired().HasMaxLength(60);
        builder.Property(l => l.Campaign).HasMaxLength(120);
        builder.Property(l => l.IpAddress).HasMaxLength(512); // IPv6 max length

        builder.Property(l => l.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        // Restrict, not cascade: an offer's redemption history must survive even if the
        // offer row itself were ever removed (offers are soft-ended in practice, never
        // deleted, but the FK constraint should not silently rely on that).
        builder.HasOne<Offer>()
            .WithMany()
            .HasForeignKey(l => l.OfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.CreatedAt);
        builder.HasIndex(l => l.Source);
        builder.HasIndex(l => l.UserType);
        builder.HasIndex(l => l.Status);
        builder.HasIndex(l => l.OfferId);
    }
}
