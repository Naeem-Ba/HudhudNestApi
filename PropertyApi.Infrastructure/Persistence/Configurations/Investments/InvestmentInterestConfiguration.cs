using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.Investments;

public sealed class InvestmentInterestConfiguration : IEntityTypeConfiguration<InvestmentInterest>
{
    public void Configure(EntityTypeBuilder<InvestmentInterest> builder)
    {
        builder.ToTable("InvestmentInterests");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InvestmentProject>()
            .WithMany()
            .HasForeignKey(i => i.InvestmentProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.UserId);

        // One interest record per (user, project) — Withdraw/Reactivate flip Status on the same
        // row instead of inserting duplicates (Phase 1 spec §31 idempotency).
        builder.HasIndex(i => new { i.UserId, i.InvestmentProjectId }).IsUnique();
    }
}
