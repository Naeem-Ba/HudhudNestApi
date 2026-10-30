using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class HostVerificationRecordConfiguration : IEntityTypeConfiguration<HostVerificationRecord>
{
    public void Configure(EntityTypeBuilder<HostVerificationRecord> builder)
    {
        builder.ToTable("HostVerificationRecords");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(r => new { r.UserId, r.Type }).IsUnique();

        // The host had no foreign key (DB audit 2026-10-29). VerifiedByAdminId stays a plain
        // audit column like every other *ByUserId column.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
