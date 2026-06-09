using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyApi.Domain.Auth.Entities;

namespace PropertyApi.Infrastructure.Auth.Configurations;

/// <summary>
/// إعداد جدول OtpCodes في قاعدة البيانات
///
/// ماذا تعني كل خاصية هنا؟
/// ─────────────────────────
/// HasMaxLength  → الحد الأقصى لطول النص (يحسّن الأداء)
/// IsRequired    → العمود لا يقبل null
/// HasIndex      → يُضيف فهرساً يُسرِّع الاستعلامات
/// </summary>
public sealed class OtpCodeConfiguration
    : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> builder)
    {
        builder.ToTable("OtpCodes");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);   // E.164 format: +963911234567

        builder.Property(o => o.CodeHash)
            .IsRequired()
            .HasMaxLength(128);  // HMAC-SHA256 base64: 44 chars + مساحة

        builder.Property(o => o.Purpose)
            .IsRequired()
            .HasConversion<int>(); // يُخزَّن كرقم في القاعدة

        builder.Property(o => o.ExpiresAt)
            .IsRequired();

        builder.Property(o => o.AttemptCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(o => o.IsUsed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(o => o.IpAddress)
            .HasMaxLength(45);   // IPv6 = 45 char max

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // ── الفهارس (Indexes) — تُسرِّع الاستعلامات ────────────
        // فهرس على (PhoneNumber + Purpose) لأنهما يُستخدمان معاً كثيراً
        builder.HasIndex(o => new { o.PhoneNumber, o.Purpose })
            .HasDatabaseName("IX_OtpCodes_PhoneNumber_Purpose");

        // فهرس على CreatedAt لحذف الرموز المنتهية تلقائياً
        builder.HasIndex(o => o.CreatedAt)
            .HasDatabaseName("IX_OtpCodes_CreatedAt");
    }
}
