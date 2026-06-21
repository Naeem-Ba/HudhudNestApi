using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Lookups.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class NeighborhoodConfiguration : IEntityTypeConfiguration<Neighborhood>
{
    public void Configure(EntityTypeBuilder<Neighborhood> builder)
    {
        builder.ToTable("Neighborhoods");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(n => n.NameEn).HasMaxLength(200);

        builder.HasIndex(n => n.DistrictId);
        builder.HasIndex(n => n.IsActive);

        builder.HasMany(n => n.Properties)
            .WithOne(p => p.Neighborhood!)
            .HasForeignKey(p => p.NeighborhoodId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}