using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Services;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialMediaAssetGeneratorTests
{
    private sealed class Fixture
    {
        public Mock<IPropertyRepository> Properties { get; } = new();
        public Mock<ISocialMediaAssetRepository> Assets { get; } = new();
        public Mock<ISocialMediaAssetStorage> Storage { get; } = new();
        public Mock<IBrandIdentityProvider> Brand { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Fixture()
        {
            Brand.Setup(x => x.GetCurrentBrand()).Returns(
                new BrandIdentity("هدهد نيست", "https://cdn.example.com/logo.png", "#0B3D59", "#D4AF37", "#FFFFFF", "#0B3D59", "Arial", 1));
            Assets.Setup(x => x.FindReusableAsync(
                It.IsAny<Guid>(), It.IsAny<SocialPlatform>(), It.IsAny<SocialAssetType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SocialMediaAsset?)null);
            Storage.Setup(x => x.SaveAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(("https://cdn.example.com/generated.svg", "public-id-1"));
        }

        public SocialMediaAssetGenerator Build() => new(
            Properties.Object, Assets.Object, Storage.Object, new MediaFolderBuilder(), Brand.Object, UnitOfWork.Object);
    }

    private static Property MakeProperty()
    {
        var property = Property.Create("شقة للبيع في طرطوس", "وصف", Guid.NewGuid(), ListingType.ForSale);
        property.PurchasePrice = 75000;
        property.Area = 120;
        property.Rooms = 3;
        property.City = "طرطوس";
        return property;
    }

    private static GenerateSocialAssetRequest MakeRequest(Guid propertyId) => new(
        propertyId, SocialPlatform.Facebook, "default", "ar", ["https://cdn.example.com/photo.jpg"], "شقة للبيع", "وصف قصير");

    [Fact]
    public async Task GenerateAsync_HappyPath_UploadsAndPersistsNewAsset()
    {
        var fixture = new Fixture();
        var property = MakeProperty();
        fixture.Properties.Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);

        var result = await fixture.Build().GenerateAsync(MakeRequest(property.Id), ct: CancellationToken.None);

        Assert.False(result.Reused);
        Assert.Equal("https://cdn.example.com/generated.svg", result.FileUrl);
        Assert.Equal("image/svg+xml", result.MimeType);
        fixture.Assets.Verify(x => x.AddAsync(It.IsAny<SocialMediaAsset>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_IdenticalRequestTwice_ReusesExistingAsset_SkipsUpload()
    {
        var fixture = new Fixture();
        var property = MakeProperty();
        fixture.Properties.Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);

        var existing = SocialMediaAsset.Create(
            property.Id, SocialPlatform.Facebook, SocialAssetType.FeedImage, "default", 1,
            "https://cdn.example.com/existing.svg", "existing-id", 1200, 630, "image/svg+xml", 2048, "will-be-overridden");

        // The generator computes its own checksum from the rendered SVG — to simulate "identical
        // content already exists" deterministically in a unit test, make FindReusableAsync match
        // unconditionally rather than trying to predict the real SHA-256 value here.
        fixture.Assets.Setup(x => x.FindReusableAsync(
                property.Id, SocialPlatform.Facebook, SocialAssetType.FeedImage, "default", SocialAssetTemplateRendererVersion(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await fixture.Build().GenerateAsync(MakeRequest(property.Id), ct: CancellationToken.None);

        Assert.True(result.Reused);
        Assert.Equal(existing.Id, result.AssetId);
        fixture.Storage.Verify(x => x.SaveAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Assets.Verify(x => x.AddAsync(It.IsAny<SocialMediaAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_ForceRegenerate_BypassesReuse_EvenIfAssetExists()
    {
        var fixture = new Fixture();
        var property = MakeProperty();
        fixture.Properties.Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);

        var existing = SocialMediaAsset.Create(
            property.Id, SocialPlatform.Facebook, SocialAssetType.FeedImage, "default", 1,
            "https://cdn.example.com/existing.svg", "existing-id", 1200, 630, "image/svg+xml", 2048, "checksum");
        fixture.Assets.Setup(x => x.FindReusableAsync(
                It.IsAny<Guid>(), It.IsAny<SocialPlatform>(), It.IsAny<SocialAssetType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await fixture.Build().GenerateAsync(MakeRequest(property.Id), forceRegenerate: true, ct: CancellationToken.None);

        Assert.False(result.Reused);
        fixture.Storage.Verify(x => x.SaveAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_PropertyNotFound_ThrowsNonRetryable()
    {
        var fixture = new Fixture();
        fixture.Properties.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Property?)null);

        var ex = await Assert.ThrowsAsync<SocialAssetGenerationException>(() =>
            fixture.Build().GenerateAsync(MakeRequest(Guid.NewGuid()), ct: CancellationToken.None));

        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task GenerateAsync_UnknownPlatformAssetTypeCombination_ThrowsNonRetryable()
    {
        var fixture = new Fixture();
        var property = MakeProperty();
        fixture.Properties.Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);

        var request = MakeRequest(property.Id) with { Platform = SocialPlatform.Telegram, AssetType = SocialAssetType.PortraitImage };

        var ex = await Assert.ThrowsAsync<SocialAssetGenerationException>(() =>
            fixture.Build().GenerateAsync(request, ct: CancellationToken.None));

        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task GenerateAsync_StorageThrows_ThrowsRetryable()
    {
        var fixture = new Fixture();
        var property = MakeProperty();
        fixture.Properties.Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        fixture.Storage.Setup(x => x.SaveAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        var ex = await Assert.ThrowsAsync<SocialAssetGenerationException>(() =>
            fixture.Build().GenerateAsync(MakeRequest(property.Id), ct: CancellationToken.None));

        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task GenerateAsync_NoValidImageUrl_StillSucceeds_WithBrandOnlyBackground()
    {
        var fixture = new Fixture();
        var property = MakeProperty();
        fixture.Properties.Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);

        var request = MakeRequest(property.Id) with { ImageUrls = ["/not-a-public-url"] };
        var result = await fixture.Build().GenerateAsync(request, ct: CancellationToken.None);

        Assert.False(result.Reused);
        Assert.NotNull(result.FileUrl);
    }

    private static int SocialAssetTemplateRendererVersion() =>
        Domain.SocialDistribution.Templates.SocialAssetTemplateRenderer.TemplateVersion;
}
