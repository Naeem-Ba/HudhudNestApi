using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.Investments;

public sealed class InvestmentRiskAssessmentConfiguration : IEntityTypeConfiguration<InvestmentRiskAssessment>
{
    public void Configure(EntityTypeBuilder<InvestmentRiskAssessment> builder)
    {
        builder.ToTable("InvestmentRiskAssessments");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RiskLevel).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.MarketRisk).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.LiquidityRisk).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ProjectRisk).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FinancingRisk).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.DeveloperRisk).HasConversion<string>().HasMaxLength(20);

        builder.Property(r => r.RiskSummary).IsRequired().HasMaxLength(4000);

        builder.HasOne<InvestmentProject>()
            .WithMany()
            .HasForeignKey(r => r.InvestmentProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // One risk assessment per project.
        builder.HasIndex(r => r.InvestmentProjectId).IsUnique();
    }
}
