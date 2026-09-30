using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialPostContentTests
{
    private static readonly Guid PublicationId = Guid.NewGuid();

    private static SocialPostContent MakeContent(
        string title = "شقة رائعة",
        string body = "شقة مساحتها 120 م² في دمشق",
        string imageUrl = "https://cdn.example.com/img.jpg",
        string targetUrl = "https://hudhudnest.com/properties/abc?utm_source=facebook",
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

    [Fact]
    public void AttachGeneratedAsset_ValidInput_UpdatesImageUrlAndAssetId_WithoutBumpingContentVersion()
    {
        var content = MakeContent();
        var assetId = Guid.NewGuid();

        content.AttachGeneratedAsset(assetId, "https://cdn.example.com/generated-asset.svg");

        Assert.Equal(assetId, content.SocialMediaAssetId);
        Assert.Equal("https://cdn.example.com/generated-asset.svg", content.ImageUrl);
        Assert.Equal(1, content.ContentVersion); // system attaching an asset is not an editorial revision
    }

    [Fact]
    public void AttachGeneratedAsset_EmptyAssetId_Throws()
    {
        var content = MakeContent();
        Assert.Throws<DomainException>(() => content.AttachGeneratedAsset(Guid.Empty, "https://cdn.example.com/a.svg"));
    }

    [Fact]
    public void AttachGeneratedAsset_InvalidUrl_Throws()
    {
        var content = MakeContent();
        Assert.Throws<DomainException>(() => content.AttachGeneratedAsset(Guid.NewGuid(), "/internal/route"));
    }

    // ── Human Review workflow (Phase 8) ────────────────────────────────────────

    [Fact]
    public void Create_DefaultsToApprovedReviewStatus()
    {
        var content = MakeContent();
        Assert.Equal(ContentReviewStatus.Approved, content.ReviewStatus);
    }

    [Fact]
    public void RequireReview_SetsPendingReview_AndClearsPriorReviewer()
    {
        var content = MakeContent();
        content.Approve(Guid.NewGuid(), DateTime.UtcNow);

        content.RequireReview("محتوى حساس.");

        Assert.Equal(ContentReviewStatus.PendingReview, content.ReviewStatus);
        Assert.Null(content.ReviewedByUserId);
        Assert.Null(content.ReviewedAt);
    }

    [Fact]
    public void Approve_FromPendingReview_SetsApprovedWithReviewer()
    {
        var content = MakeContent();
        content.RequireReview("test");
        var reviewer = Guid.NewGuid();

        content.Approve(reviewer, DateTime.UtcNow, "يبدو جيداً.");

        Assert.Equal(ContentReviewStatus.Approved, content.ReviewStatus);
        Assert.Equal(reviewer, content.ReviewedByUserId);
    }

    [Fact]
    public void Reject_WithoutNote_Throws()
    {
        var content = MakeContent();
        content.RequireReview("test");
        Assert.Throws<DomainException>(() => content.Reject(Guid.NewGuid(), "  ", DateTime.UtcNow));
    }

    [Fact]
    public void Reject_WithNote_SetsRejectedWithReviewer()
    {
        var content = MakeContent();
        content.RequireReview("test");
        var reviewer = Guid.NewGuid();

        content.Reject(reviewer, "يتعارض مع بيانات العقار.", DateTime.UtcNow);

        Assert.Equal(ContentReviewStatus.Rejected, content.ReviewStatus);
        Assert.Equal(reviewer, content.ReviewedByUserId);
        Assert.Equal("يتعارض مع بيانات العقار.", content.ReviewNote);
    }

    [Fact]
    public void Revise_AfterRejection_ReturnsToPendingReview_NeverSilentlyApproved()
    {
        var content = MakeContent();
        content.RequireReview("test");
        content.Reject(Guid.NewGuid(), "خطأ", DateTime.UtcNow);

        content.Revise("عنوان معدّل", "نص معدّل بعد الرفض", "https://cdn.example.com/new.jpg", null);

        Assert.Equal(ContentReviewStatus.PendingReview, content.ReviewStatus);
    }
}
