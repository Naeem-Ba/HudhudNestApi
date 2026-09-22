using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialContentPolicyTests
{
    [Fact]
    public void SanitizePlainText_StripsHtmlTags()
    {
        Assert.Equal("مرحباً", SocialContentPolicy.SanitizePlainText("<b>مرحباً</b>"));
    }

    [Fact]
    public void SanitizePlainText_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SocialContentPolicy.SanitizePlainText(null));
        Assert.Equal(string.Empty, SocialContentPolicy.SanitizePlainText("   "));
    }

    [Fact]
    public void SanitizePlainText_PreservesNewlines()
    {
        var result = SocialContentPolicy.SanitizePlainText("سطر أول\nسطر ثاني");
        Assert.Contains("\n", result);
    }

    [Fact]
    public void NormalizeHashtags_CapsAtPlatformLimit()
    {
        var many = Enumerable.Range(0, 50).Select(i => $"tag{i}");
        var normalized = SocialContentPolicy.NormalizeHashtags(many, SocialPlatform.Facebook);

        Assert.Equal(SocialContentPolicy.GetLimits(SocialPlatform.Facebook).MaxHashtags, normalized.Count);
    }

    [Fact]
    public void NormalizeHashtags_DropsInvalidEntries_KeepsValidOnes()
    {
        var normalized = SocialContentPolicy.NormalizeHashtags(new[] { "valid_tag", "has space", "<script>", "" }, SocialPlatform.Facebook);

        Assert.Single(normalized);
        Assert.Equal("valid_tag", normalized[0]);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("en", true)]
    [InlineData("AR", true)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedLanguage_MatchesDocumentedSet(string? language, bool expected) =>
        Assert.Equal(expected, SocialContentPolicy.IsSupportedLanguage(language));

    [Theory]
    [InlineData("https://example.com/img.jpg", true)]
    [InlineData("http://example.com/img.jpg", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/internal/route", false)]
    [InlineData("", false)]
    public void IsValidPublicUrl_RejectsNonHttpAndRelativeUrls(string url, bool expected) =>
        Assert.Equal(expected, SocialContentPolicy.IsValidPublicUrl(url));
}
