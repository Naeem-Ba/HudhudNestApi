using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.Investments;

public sealed class InvestmentUpdateConfiguration : IEntityTypeConfiguration<InvestmentUpdate>
{
    public void Configure(EntityTypeBuilder<InvestmentUpdate> builder)
    {
        builder.ToTable("InvestmentUpdates");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Title).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Content).IsRequired().HasMaxLength(8000);
        builder.Property(u => u.UpdateType).HasConversion<string>().HasMaxLength(30);

        builder.HasOne<InvestmentProject>()
            .WithMany()
            .HasForeignKey(u => u.InvestmentProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(u => new { u.InvestmentProjectId, u.PublishedAt });

        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
