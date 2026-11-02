using HudhudNestApi.Domain.Referrals.Entities;
using HudhudNestApi.Domain.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ReferralCodeConfiguration : IEntityTypeConfiguration<ReferralCode>
{
    public void Configure(EntityTypeBuilder<ReferralCode> builder)
    {
        builder.ToTable("ReferralCodes");
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.Code).HasMaxLength(ReferralCode.CodeLength).IsFixedLength().IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReferralConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> builder)
    {
        builder.ToTable("Referrals", t =>
        {
            t.HasCheckConstraint("CK_Referrals_NotSelf", "\"ReferrerUserId\" <> \"ReferredUserId\"");
            t.HasCheckConstraint("CK_Referrals_Status", "\"Status\" BETWEEN 1 AND 4");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Code).HasMaxLength(ReferralCode.CodeLength).IsFixedLength().IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(Referral.ReasonMaxLength);

        builder.HasOne<UserAccount>().WithMany().HasForeignKey(r => r.ReferrerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(r => r.ReferredUserId).OnDelete(DeleteBehavior.Cascade);

        // A person can be referred once.
        builder.HasIndex(r => r.ReferredUserId).IsUnique();
        builder.HasIndex(r => new { r.ReferrerUserId, r.Status });
        // The qualification job scans pending rows only.
        builder.HasIndex(r => r.CreatedAt).HasFilter("\"Status\" = 1").HasDatabaseName("IX_Referrals_Pending");
    }
}
