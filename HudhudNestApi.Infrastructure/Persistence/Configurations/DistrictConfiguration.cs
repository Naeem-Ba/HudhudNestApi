using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("Districts");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.NameAr).IsRequired().HasMaxLength(200);
        builder.Property(d => d.NameEn).HasMaxLength(200);

        builder.HasIndex(d => d.GovernorateId);
        builder.HasIndex(d => d.IsActive);

        builder.HasMany(d => d.Neighborhoods)
            .WithOne(n => n.District!)
            .HasForeignKey(n => n.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.Properties)
            .WithOne(p => p.District!)
            .HasForeignKey(p => p.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}