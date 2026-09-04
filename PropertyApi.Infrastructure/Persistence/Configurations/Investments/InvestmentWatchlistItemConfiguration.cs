using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.Investments;

public sealed class InvestmentWatchlistItemConfiguration : IEntityTypeConfiguration<InvestmentWatchlistItem>
{
    public void Configure(EntityTypeBuilder<InvestmentWatchlistItem> builder)
    {
        builder.ToTable("InvestmentWatchlistItems");
        builder.HasKey(w => w.Id);

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InvestmentProject>()
            .WithMany()
            .HasForeignKey(w => w.InvestmentProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => w.UserId);

        // A user can watch a given project only once — DB-enforced, not just application-checked
        // (Phase 1 spec §11/§36).
        builder.HasIndex(w => new { w.UserId, w.InvestmentProjectId }).IsUnique();
    }
}
