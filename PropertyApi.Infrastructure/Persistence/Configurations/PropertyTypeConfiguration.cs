using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class PropertyTypeConfiguration : IEntityTypeConfiguration<PropertyType>
{
    public void Configure(EntityTypeBuilder<PropertyType> builder)
    {
        builder.ToTable("PropertyTypes");

        builder.HasKey(pt => pt.Id);

        builder.Property(pt => pt.Code)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(pt => pt.NameAr)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pt => pt.NameEn)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pt => pt.Category)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(pt => pt.Icon)
            .HasMaxLength(100);

        builder.HasIndex(pt => pt.Code)
            .IsUnique();

        builder.HasIndex(pt => pt.Category);
        builder.HasIndex(pt => pt.IsActive);

        builder.HasMany(pt => pt.Properties)
            .WithOne(p => p.PropertyType!)
            .HasForeignKey(p => p.PropertyTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}