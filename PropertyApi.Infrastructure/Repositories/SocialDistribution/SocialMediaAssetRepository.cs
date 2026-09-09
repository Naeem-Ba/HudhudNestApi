using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories.SocialDistribution;

public sealed class SocialMediaAssetRepository : ISocialMediaAssetRepository
{
    private readonly AppDbContext _db;

    public SocialMediaAssetRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SocialMediaAsset asset, CancellationToken ct = default) =>
        await _db.SocialMediaAssets.AddAsync(asset, ct);

    public Task<SocialMediaAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SocialMediaAssets.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<SocialMediaAsset?> FindReusableAsync(
        Guid propertyId, SocialPlatform platform, SocialAssetType assetType, string templateId, int templateVersion,
        string checksum, CancellationToken ct = default) =>
        _db.SocialMediaAssets.FirstOrDefaultAsync(a =>
            a.PropertyId == propertyId &&
            a.Platform == platform &&
            a.AssetType == assetType &&
            a.TemplateId == templateId &&
            a.TemplateVersion == templateVersion &&
            a.Checksum == checksum &&
            a.Status == SocialMediaAssetStatus.Generated,
            ct);

    public void Update(SocialMediaAsset asset) => _db.SocialMediaAssets.Update(asset);
}
