using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Auth.Entities;

namespace PropertyApi.Infrastructure.Persistence.Configurations;

public sealed class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> builder)
    {
        builder.ToTable("OtpCodes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.CodeHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.IpAddress)
            .HasMaxLength(64);

        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("IX_OtpCodes_ExpiresAt");

        builder.HasIndex(x => new { x.PhoneNumber, x.Purpose, x.ExpiresAt })
            .HasDatabaseName("IX_OtpCodes_Phone_Purpose_ExpiresAt");

        builder.HasIndex(x => new { x.PhoneNumber, x.CreatedAt })
            .HasDatabaseName("IX_OtpCodes_Phone_CreatedAt");
    }
}
