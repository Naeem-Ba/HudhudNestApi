using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.Investments;

public sealed class InvestmentDocumentConfiguration : IEntityTypeConfiguration<InvestmentDocument>
{
    public void Configure(EntityTypeBuilder<InvestmentDocument> builder)
    {
        builder.ToTable("InvestmentDocuments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DocumentType).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(300);
        builder.Property(d => d.StorageProvider).IsRequired().HasMaxLength(50);
        builder.Property(d => d.StorageKey).IsRequired().HasMaxLength(500);
        builder.Property(d => d.Url).IsRequired().HasMaxLength(2000);
        builder.Property(d => d.DocumentHash).HasMaxLength(128);

        builder.HasOne<InvestmentProject>()
            .WithMany()
            .HasForeignKey(d => d.InvestmentProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.InvestmentProjectId);
        builder.HasIndex(d => new { d.InvestmentProjectId, d.IsPublic });

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
