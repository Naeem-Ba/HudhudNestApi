using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialMediaAssetTests
{
    private static SocialMediaAsset MakeAsset() => SocialMediaAsset.Create(
        Guid.NewGuid(), SocialPlatform.Facebook, SocialAssetType.FeedImage, "default", 1,
        "https://cdn.example.com/a.svg", "public-id-1", 1200, 630, "image/svg+xml", 2048, "abc123checksum");

    [Fact]
    public void Create_ValidInput_StartsGenerated()
    {
        var asset = MakeAsset();
        Assert.Equal(SocialMediaAssetStatus.Generated, asset.Status);
        Assert.Null(asset.PublicationId);
    }

    [Fact]
    public void Create_EmptyPropertyId_Throws() =>
        Assert.Throws<DomainException>(() => SocialMediaAsset.Create(
            Guid.Empty, SocialPlatform.Facebook, SocialAssetType.FeedImage, "default", 1,
            "https://cdn.example.com/a.svg", "id", 100, 100, "image/svg+xml", 10, "checksum"));

    [Fact]
    public void Create_ZeroDimensions_Throws() =>
        Assert.Throws<DomainException>(() => SocialMediaAsset.Create(
            Guid.NewGuid(), SocialPlatform.Facebook, SocialAssetType.FeedImage, "default", 1,
            "https://cdn.example.com/a.svg", "id", 0, 100, "image/svg+xml", 10, "checksum"));

    [Fact]
    public void Create_MissingChecksum_Throws() =>
        Assert.Throws<DomainException>(() => SocialMediaAsset.Create(
            Guid.NewGuid(), SocialPlatform.Facebook, SocialAssetType.FeedImage, "default", 1,
            "https://cdn.example.com/a.svg", "id", 100, 100, "image/svg+xml", 10, ""));

    [Fact]
    public void AttachToPublication_SetsPublicationId()
    {
        var asset = MakeAsset();
        var publicationId = Guid.NewGuid();

        asset.AttachToPublication(publicationId);

        Assert.Equal(publicationId, asset.PublicationId);
    }

    [Fact]
    public void MarkExpired_ChangesStatus()
    {
        var asset = MakeAsset();
        asset.MarkExpired();
        Assert.Equal(SocialMediaAssetStatus.Expired, asset.Status);
    }
}
