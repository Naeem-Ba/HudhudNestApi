using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.SocialDistribution.Policies;
using HudhudNestApi.Domain.SocialDistribution.Templates;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>Phase 7 spec §31.E — the deterministic Template Engine itself, tested with no storage/network/DB involved.</summary>
public sealed class SocialAssetTemplateRendererTests
{
    private static readonly BrandIdentity Brand = new(
        "هدهد نيست", "https://cdn.example.com/logo.png", "#0B3D59", "#D4AF37", "#FFFFFF", "#0B3D59", "Arial", 1);

    private static SocialAssetTemplateContext MakeContext(
        string title = "شقة رائعة للبيع", string language = "ar", string? sourceImageUrl = "https://cdn.example.com/property.jpg") => new(
        Guid.NewGuid(), SocialAssetTemplateRenderer.TemplateId, SocialAssetTemplateRenderer.TemplateVersion,
        SocialPlatform.Facebook, SocialAssetType.FeedImage, language, title,
        "طرطوس", "75,000 USD", "120 م²", "3 غرف", "شقة", "للبيع", sourceImageUrl, Brand);

    private static readonly SocialAssetPresetCatalog.Preset FacebookFeedPreset =
        SocialAssetPresetCatalog.TryGetPreset(SocialPlatform.Facebook, SocialAssetType.FeedImage)!;

    [Fact]
    public void Render_SameInputTwice_ProducesByteIdenticalOutput()
    {
        var context = MakeContext();

        var first = SocialAssetTemplateRenderer.Render(context, FacebookFeedPreset);
        var second = SocialAssetTemplateRenderer.Render(context, FacebookFeedPreset);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Render_ProducesValidSvgWithCorrectDimensions()
    {
        var svg = SocialAssetTemplateRenderer.Render(MakeContext(), FacebookFeedPreset);

        Assert.Contains("<svg", svg);
        Assert.Contains($"width=\"{FacebookFeedPreset.Width}\"", svg);
        Assert.Contains($"height=\"{FacebookFeedPreset.Height}\"", svg);
    }

    [Fact]
    public void Render_ArabicLanguage_SetsRtlDirection()
    {
        var svg = SocialAssetTemplateRenderer.Render(MakeContext(language: "ar"), FacebookFeedPreset);
        Assert.Contains("dir=\"rtl\"", svg);
    }

    [Fact]
    public void Render_EnglishLanguage_SetsLtrDirection()
    {
        var svg = SocialAssetTemplateRenderer.Render(MakeContext(language: "en"), FacebookFeedPreset);
        Assert.Contains("dir=\"ltr\"", svg);
    }

    [Fact]
    public void Render_MaliciousTitle_IsEscapedNeverEmittedAsMarkup()
    {
        var svg = SocialAssetTemplateRenderer.Render(MakeContext(title: "<script>alert(1)</script>"), FacebookFeedPreset);

        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&lt;script&gt;", svg);
    }

    [Fact]
    public void Render_NoSourceImage_StillProducesValidOutput_WithoutAPhotoLayer()
    {
        var svg = SocialAssetTemplateRenderer.Render(MakeContext(sourceImageUrl: null), FacebookFeedPreset);

        Assert.Contains("<svg", svg);
        // The brand logo is still a legitimate <image> element — only the property photo layer
        // (which would reference property.jpg) must be absent when there is no source image.
        Assert.DoesNotContain("property.jpg", svg);
    }

    [Fact]
    public void Render_WithSourceImage_EmbedsImageReference()
    {
        var svg = SocialAssetTemplateRenderer.Render(MakeContext(), FacebookFeedPreset);
        Assert.Contains("<image href=\"https://cdn.example.com/property.jpg\"", svg);
    }

    [Fact]
    public void Render_VeryLongTitle_IsTruncatedSafely()
    {
        var longTitle = new string('ش', 200);
        var svg = SocialAssetTemplateRenderer.Render(MakeContext(title: longTitle), FacebookFeedPreset);

        Assert.Contains("…", svg);
        Assert.DoesNotContain(longTitle, svg);
    }

    [Fact]
    public void FormatPrice_UsesInvariantCulture_RegardlessOfCurrentThreadCulture()
    {
        var formatted = SocialAssetTemplateRenderer.FormatPrice(75000m, "USD");
        Assert.Equal("75,000 USD", formatted);
    }
}
