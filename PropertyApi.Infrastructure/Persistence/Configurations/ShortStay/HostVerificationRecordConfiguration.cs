using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations.ShortStay;

public sealed class HostVerificationRecordConfiguration : IEntityTypeConfiguration<HostVerificationRecord>
{
    public void Configure(EntityTypeBuilder<HostVerificationRecord> builder)
    {
        builder.ToTable("HostVerificationRecords");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(r => new { r.UserId, r.Type }).IsUnique();
    }
}
