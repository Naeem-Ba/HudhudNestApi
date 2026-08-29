using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class ShortStayListingPhotoConfiguration : IEntityTypeConfiguration<ShortStayListingPhoto>
{
    public void Configure(EntityTypeBuilder<ShortStayListingPhoto> builder)
    {
        builder.ToTable("ShortStayListingPhotos");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Url).IsRequired().HasMaxLength(1000);
        builder.Property(p => p.PublicId).IsRequired().HasMaxLength(300);
        builder.Property(p => p.ThumbnailUrl).HasMaxLength(1000);
        builder.Property(p => p.AltText).HasMaxLength(300);

        builder.HasIndex(p => p.ShortStayListingId);
    }
}
