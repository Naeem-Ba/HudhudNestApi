using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Marketing.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class SurveyResponseConfiguration : IEntityTypeConfiguration<SurveyResponse>
{
    public void Configure(EntityTypeBuilder<SurveyResponse> builder)
    {
        builder.ToTable("SurveyResponses");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.WillingnessToPay).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.PreferredPaymentModel).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.ExpectedMonthlyPriceUsd).HasColumnType("numeric(10,2)");
        builder.Property(s => s.ExpectedPerListingPriceUsd).HasColumnType("numeric(10,2)");
        builder.Property(s => s.AcceptableCommissionPercent).HasColumnType("numeric(5,2)");
        builder.Property(s => s.MostImportantFeature).HasMaxLength(300);
        builder.Property(s => s.BiggestProblem).HasMaxLength(500);
        builder.Property(s => s.SubscriptionBlocker).HasMaxLength(500);
        builder.Property(s => s.SimilarToolName).HasMaxLength(150);
        builder.Property(s => s.Source).IsRequired().HasMaxLength(60);

        // Restrict, not cascade — a survey response is standalone market-research data and
        // must survive even in the hypothetical case a Lead row were ever removed.
        builder.HasOne<Lead>()
            .WithMany()
            .HasForeignKey(s => s.LeadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.CreatedAt);
        builder.HasIndex(s => s.LeadId);
        builder.HasIndex(s => s.WillingnessToPay);
        builder.HasIndex(s => s.PreferredPaymentModel);
    }
}
