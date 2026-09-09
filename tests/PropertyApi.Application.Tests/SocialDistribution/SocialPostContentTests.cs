using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.Tests.SocialDistribution;

public sealed class SocialPostContentTests
{
    private static readonly Guid PublicationId = Guid.NewGuid();

    private static SocialPostContent MakeContent(
        string title = "شقة رائعة",
        string body = "شقة مساحتها 120 م² في دمشق",
        string imageUrl = "https://cdn.example.com/img.jpg",
        string targetUrl = "https://realestateworld.world/properties/abc?utm_source=facebook",
        IEnumerable<string>? hashtags = null,
        string language = "ar",
        SocialPlatform platform = SocialPlatform.Facebook) =>
        SocialPostContent.Create(PublicationId, platform, title, body, imageUrl, targetUrl, hashtags, language);

    [Fact]
    public void Create_ValidInput_Succeeds()
    {
        var content = MakeContent();

        Assert.Equal("شقة رائعة", content.Title);
        Assert.Equal("ar", content.Language);
        Assert.Equal(1, content.ContentVersion);
    }

    [Fact]
    public void Create_StripsHtmlFromTitleAndBody()
    {
        var content = MakeContent(title: "<b>عرض</b> خاص", body: "<script>alert(1)</script>نص آمن");

        Assert.DoesNotContain("<", content.Title);
        Assert.DoesNotContain("<script>", content.Body);
        Assert.Contains("نص آمن", content.Body);
    }

    [Fact]
    public void Create_BlankTitle_Throws() =>
        Assert.Throws<DomainException>(() => MakeContent(title: "   "));

    [Fact]
    public void Create_TitleExceedingPlatformLimit_Throws() =>
        Assert.Throws<DomainException>(() => MakeContent(title: new string('a', 500)));

    [Fact]
    public void Create_NonPublicImageUrl_Throws() =>
        Assert.Throws<DomainException>(() => MakeContent(imageUrl: "/local/path.jpg"));

    [Fact]
    public void Create_JavascriptTargetUrl_Throws() =>
        Assert.Throws<DomainException>(() => MakeContent(targetUrl: "javascript:alert(1)"));

    [Fact]
    public void Create_UnsupportedLanguage_Throws() =>
        Assert.Throws<DomainException>(() => MakeContent(language: "fr"));

    [Fact]
    public void Create_NormalizesHashtags_StripsHashAndInvalidEntries()
    {
        var content = MakeContent(hashtags: new[] { "#عقارات", "دمشق", "invalid tag with spaces", "عقارات" });

        Assert.Contains("عقارات", content.HashtagList);
        Assert.Contains("دمشق", content.HashtagList);
        Assert.DoesNotContain(content.HashtagList, t => t.Contains(' '));
        // Deduplicated: "عقارات" appears once even though supplied twice (once with '#').
        Assert.Single(content.HashtagList, t => t == "عقارات");
    }

    [Fact]
    public void Create_NoHashtags_ProducesEmptyList()
    {
        var content = MakeContent(hashtags: null);
        Assert.Empty(content.HashtagList);
    }

    [Fact]
    public void Revise_ValidInput_BumpsContentVersion()
    {
        var content = MakeContent();

        content.Revise("عنوان جديد", "نص جديد للمنشور", "https://cdn.example.com/new.jpg", new[] { "جديد" });

        Assert.Equal(2, content.ContentVersion);
        Assert.Equal("عنوان جديد", content.Title);
    }
}
