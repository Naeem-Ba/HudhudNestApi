using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class AccommodationTypeConfiguration : IEntityTypeConfiguration<AccommodationType>
{
    public void Configure(EntityTypeBuilder<AccommodationType> builder)
    {
        builder.ToTable("AccommodationTypes");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).IsRequired().HasMaxLength(50);
        builder.Property(t => t.NameAr).IsRequired().HasMaxLength(100);
        builder.Property(t => t.NameEn).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Icon).HasMaxLength(50);

        builder.HasIndex(t => t.Code).IsUnique();
        builder.HasIndex(t => t.IsActive);
    }
}
