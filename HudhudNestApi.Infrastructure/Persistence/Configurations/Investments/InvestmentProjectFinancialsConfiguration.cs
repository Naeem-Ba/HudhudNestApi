using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Investments.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.Investments;

public sealed class InvestmentProjectFinancialsConfiguration : IEntityTypeConfiguration<InvestmentProjectFinancials>
{
    public void Configure(EntityTypeBuilder<InvestmentProjectFinancials> builder)
    {
        builder.ToTable("InvestmentProjectFinancials");
        builder.HasKey(f => f.Id);

        foreach (var moneyProperty in new[]
                 {
                     nameof(InvestmentProjectFinancials.PurchasePrice),
                     nameof(InvestmentProjectFinancials.RenovationCost),
                     nameof(InvestmentProjectFinancials.ConstructionCost),
                     nameof(InvestmentProjectFinancials.Taxes),
                     nameof(InvestmentProjectFinancials.NotaryCost),
                     nameof(InvestmentProjectFinancials.BrokerCost),
                     nameof(InvestmentProjectFinancials.FinancingCost),
                     nameof(InvestmentProjectFinancials.OperatingCost),
                     nameof(InvestmentProjectFinancials.ContingencyReserve),
                     nameof(InvestmentProjectFinancials.ExpectedRevenue),
                     nameof(InvestmentProjectFinancials.ExpectedProfit),
                     nameof(InvestmentProjectFinancials.TotalProjectCost),
                 })
        {
            builder.Property(moneyProperty).HasColumnType("decimal(18,4)");
        }

        builder.HasOne<InvestmentProject>()
            .WithMany()
            .HasForeignKey(f => f.InvestmentProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // One financials record per project.
        builder.HasIndex(f => f.InvestmentProjectId).IsUnique();
    }
}
