using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class GovernorateConfiguration : IEntityTypeConfiguration<Governorate>
{
    public void Configure(EntityTypeBuilder<Governorate> builder)
    {
        builder.ToTable("Governorates");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(g => g.NameEn).IsRequired().HasMaxLength(200);
        builder.Property(g => g.CountryCode).IsRequired().HasMaxLength(5);

        builder.HasIndex(g => g.CountryCode);
        builder.HasIndex(g => g.IsActive);

        builder.HasMany(g => g.Districts)
            .WithOne(d => d.Governorate!)
            .HasForeignKey(d => d.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(g => g.Properties)
            .WithOne(p => p.Governorate!)
            .HasForeignKey(p => p.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}