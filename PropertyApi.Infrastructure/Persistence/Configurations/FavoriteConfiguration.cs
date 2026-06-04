using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("Favorites");

        // Composite PK — no Id needed
        builder.HasKey(f => new { f.UserId, f.PropertyId });

        builder.HasOne(f => f.User)
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Property)
            .WithMany(p => p.Favorites)
            .HasForeignKey(f => f.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => f.PropertyId);
    }
}
