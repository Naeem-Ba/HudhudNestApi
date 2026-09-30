using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Audit.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(log => log.IpAddress)
            .HasMaxLength(64);

        builder.Property(log => log.Timestamp)
            .IsRequired();

        builder.Property(log => log.OldValue)
            .HasColumnType("text");

        builder.Property(log => log.NewValue)
            .HasColumnType("text");

        builder.HasOne(log => log.User)
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(log => log.UserId);
        builder.HasIndex(log => log.Action);
        builder.HasIndex(log => log.Timestamp);
    }
}
