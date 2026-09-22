using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ServiceOfferingConfiguration : IEntityTypeConfiguration<ServiceOffering>
{
    public void Configure(EntityTypeBuilder<ServiceOffering> builder)
    {
        builder.ToTable("ServiceOfferings");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Category)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(o => o.Title).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Description).HasMaxLength(3000);
        builder.Property(o => o.BasePrice).HasColumnType("decimal(18,2)");

        builder.HasOne(o => o.ServiceProvider)
            .WithMany()
            .HasForeignKey(o => o.ServiceProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(o => o.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.ServiceProviderId);
        builder.HasIndex(o => o.Category);
        builder.HasIndex(o => new { o.Category, o.IsActive });

        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}
