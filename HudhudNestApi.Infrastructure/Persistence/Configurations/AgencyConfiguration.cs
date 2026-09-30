using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Infrastructure.Persistence.Configurations;

public sealed class AgencyConfiguration : IEntityTypeConfiguration<Agency>
{
    public void Configure(EntityTypeBuilder<Agency> builder)
    {
        builder.ToTable("Agencies");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.Slug)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.Property(a => a.LogoUrl)
            .HasMaxLength(2048);

        builder.Property(a => a.LogoPublicId)
            .HasMaxLength(255);

        builder.Property(a => a.ContactEmail)
            .HasMaxLength(320);

        builder.Property(a => a.ContactPhone)
            .HasMaxLength(50);

        builder.Property(a => a.City)
            .HasMaxLength(120);

        builder.Property(a => a.CountryCode)
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired();

        builder.Property(a => a.LicenseNumber)
            .HasMaxLength(120);

        // Stage 8 (Admin Dashboard) — manual-review flag. See Agency.FlagForManualReview's
        // own doc comment for why this lives directly on Agency instead of a new table.
        builder.Property(a => a.RequiresManualReview)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.ManualReviewReason)
            .HasMaxLength(2000);

        // Referential integrity for the owner, matching every other UserAccount-owning
        // relationship in the schema (AgencyInvitation.InviterUserId/TargetUserId, etc.).
        // Restrict: an owner must transfer ownership or deactivate the agency before their
        // account can be removed, never lose the link silently.
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(a => a.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique among live agencies only. Filtered rather than plain, so a deleted
        // agency's slug is released instead of being reserved forever by a soft-deleted row
        // that no query can even see.
        builder.HasIndex(a => a.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Agencies_Slug")
            .HasFilter("\"IsDeleted\" = false");

        // One agency per owner, enforced in the database rather than only in the handler:
        // two concurrent create requests would both pass the handler's check.
        builder.HasIndex(a => a.OwnerUserId)
            .IsUnique()
            .HasDatabaseName("IX_Agencies_OwnerUserId")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(a => new { a.CountryCode, a.City })
            .HasDatabaseName("IX_Agencies_CountryCode_City");

        // ── الموقع الجغرافي المنظَّم ──────────────────────────────────────
        // نفس العلاقات ونفس DeleteBehavior.Restrict المستخدمَين في
        // PropertyConfiguration لـ Governorate/District/Neighborhood — لا يمكن
        // حذف محافظة/منطقة/حي طالما مكتب عقاري يشير إليها. Restrict لا SetNull:
        // هذه الجداول المرجعية (seed ثابت) لا تُحذف في التشغيل العادي أصلًا.
        builder.HasOne(a => a.Governorate)
            .WithMany()
            .HasForeignKey(a => a.GovernorateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.District)
            .WithMany()
            .HasForeignKey(a => a.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Neighborhood)
            .WithMany()
            .HasForeignKey(a => a.NeighborhoodId)
            .OnDelete(DeleteBehavior.Restrict);

        // فهارس على الأعمدة الجديدة — نفس نمط PropertyConfiguration
        // (IX_Properties_GovernorateId وما شابه)، مفيدة لاحقًا لأي مطابقة
        // جغرافية بين المكاتب والإعلانات.
        builder.HasIndex(a => a.GovernorateId);
        builder.HasIndex(a => a.DistrictId);
        builder.HasIndex(a => a.NeighborhoodId);

        // Lets the admin dashboard list flagged offices without a table scan.
        builder.HasIndex(a => a.RequiresManualReview)
            .HasDatabaseName("IX_Agencies_RequiresManualReview");

        // Same soft-delete convention every other entity here follows, so a deleted agency
        // disappears from every read path without each query having to remember.
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
