using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Domain.SocialDistribution.Entities;

/// <summary>
/// One template-generated social media image (Phase 7 spec §22) — e.g. "the branded Instagram
/// square image for property X, template v1". Deliberately its own aggregate, never a field on
/// <c>Property</c> or <c>SocialPostContent</c> directly (the same "keep generated/derivative
/// artifacts out of the core entity" principle Phase 3 already applied to
/// <see cref="SocialPostContent"/> itself) — a property can accumulate many assets over time
/// (one per platform/asset-type/template-version), and <see cref="SocialPostContent"/> merely
/// references the one it currently uses via <see cref="SocialPostContent.SocialMediaAssetId"/>.
///
/// Never hard-deleted (soft delete via <see cref="BaseEntity.IsDeleted"/>) — an asset that backed
/// a Published post must remain traceable even after a newer template version supersedes it for
/// future publications (spec: "لا تحذف Asset مستخدماً في Publication منشورة").
/// </summary>
public sealed class SocialMediaAsset : BaseEntity
{
    private SocialMediaAsset() { }

    public Guid PropertyId { get; private set; }

    /// <summary>Set once the asset is actually attached to a publication's content — null for a freshly-generated, not-yet-used asset.</summary>
    public Guid? PublicationId { get; private set; }

    public SocialPlatform Platform { get; private set; }

    public SocialAssetType AssetType { get; private set; }

    public string TemplateId { get; private set; } = string.Empty;

    public int TemplateVersion { get; private set; }

    /// <summary>Public, CDN-hosted URL — never a signed/expiring link a publisher could fail to fetch later (spec §21).</summary>
    public string FileUrl { get; private set; } = string.Empty;

    /// <summary>Storage-provider-specific identifier (e.g. Cloudinary PublicId), needed to delete/manage the underlying object later.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public string MimeType { get; private set; } = string.Empty;

    public long FileSizeBytes { get; private set; }

    /// <summary>SHA-256 hex digest of the rendered content — the dedupe key alongside (PropertyId, Platform, AssetType, TemplateId, TemplateVersion). See <see cref="Application.SocialDistribution.Services.SocialMediaAssetGenerator"/>.</summary>
    public string Checksum { get; private set; } = string.Empty;

    public SocialMediaAssetStatus Status { get; private set; } = SocialMediaAssetStatus.Pending;

    public string? ErrorMessage { get; private set; }

    public static SocialMediaAsset Create(
        Guid propertyId,
        SocialPlatform platform,
        SocialAssetType assetType,
        string templateId,
        int templateVersion,
        string fileUrl,
        string storageKey,
        int width,
        int height,
        string mimeType,
        long fileSizeBytes,
        string checksum)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("معرّف العقار مطلوب لإنشاء Asset.");

        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new DomainException("رابط الملف مطلوب.");

        if (string.IsNullOrWhiteSpace(checksum))
            throw new DomainException("Checksum مطلوب لمنع التكرار.");

        if (width <= 0 || height <= 0)
            throw new DomainException("أبعاد الصورة يجب أن تكون أكبر من صفر.");

        return new SocialMediaAsset
        {
            PropertyId = propertyId,
            Platform = platform,
            AssetType = assetType,
            TemplateId = templateId.Trim(),
            TemplateVersion = templateVersion,
            FileUrl = fileUrl.Trim(),
            StorageKey = storageKey.Trim(),
            Width = width,
            Height = height,
            MimeType = mimeType,
            FileSizeBytes = fileSizeBytes,
            Checksum = checksum.Trim(),
            Status = SocialMediaAssetStatus.Generated,
        };
    }

    /// <summary>Records which publication actually ended up using this asset — purely informational/traceability, never re-attaches the asset elsewhere.</summary>
    public void AttachToPublication(Guid publicationId)
    {
        if (publicationId == Guid.Empty)
            throw new DomainException("معرّف المنشور مطلوب.");

        PublicationId = publicationId;
    }

    public void MarkExpired() => Status = SocialMediaAssetStatus.Expired;
}
