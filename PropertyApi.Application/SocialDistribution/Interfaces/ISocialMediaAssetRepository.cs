using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

public interface ISocialMediaAssetRepository
{
    Task AddAsync(SocialMediaAsset asset, CancellationToken ct = default);

    Task<SocialMediaAsset?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Phase 7 dedupe (spec §22: "لا تنشئ Asset مكرراً إذا كان نفس Checksum وTemplateVersion
    /// صالحين"). A match means: same property, platform, asset type, template, template version,
    /// and byte-for-byte identical rendered content — reuse it instead of re-uploading.
    /// </summary>
    Task<SocialMediaAsset?> FindReusableAsync(
        Guid propertyId,
        SocialPlatform platform,
        SocialAssetType assetType,
        string templateId,
        int templateVersion,
        string checksum,
        CancellationToken ct = default);

    void Update(SocialMediaAsset asset);
}
