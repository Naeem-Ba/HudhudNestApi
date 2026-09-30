using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("ConsentRecords");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PolicyType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.PolicyVersion)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(x => x.ConsentedAtUtc)
            .IsRequired();

        // A user may re-consent to the same policy across versions, or after withdrawing,
        // so the natural key is (UserId, PolicyType, PolicyVersion) — not unique on its
        // own, since re-recording the SAME version after a withdrawal is legitimate and
        // must not silently overwrite the earlier, now-superseded row.
        builder.HasIndex(x => new { x.UserId, x.PolicyType });
        builder.HasIndex(x => x.ConsentedAtUtc);

        // Restrict, not cascade: a UserAccount is only ever soft-deleted in this codebase,
        // but a consent record must never be allowed to disappear via a cascade delete even
        // in principle — it is the audit trail this entity exists to preserve.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
