using HudhudNestApi.Application.SocialDistribution.Services;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>
/// The stored property photo is whatever the owner uploaded (any format, any size); the platforms
/// are stricter — Instagram's Content Publishing API accepts JPEG only, and Telegram's
/// <c>sendPhoto</c> by URL caps the file at 5 MB. Cloudinary URLs are normalised to a bounded JPEG
/// at publish time; anything that is not a Cloudinary image delivery URL passes through untouched.
/// </summary>
public sealed class SocialImageUrlTransformerTests
{
    private const string Transformation = "f_jpg,q_auto,w_1440,c_limit";

    [Theory]
    [InlineData(
        "https://res.cloudinary.com/demo/image/upload/v1/property-images/abc.png",
        $"https://res.cloudinary.com/demo/image/upload/{Transformation}/v1/property-images/abc.jpg")]
    [InlineData(
        "https://res.cloudinary.com/demo/image/upload/property-images/abc.webp",
        $"https://res.cloudinary.com/demo/image/upload/{Transformation}/property-images/abc.jpg")]
    [InlineData(
        "https://res.cloudinary.com/demo/image/upload/v1700000000/p/abc.HEIC",
        $"https://res.cloudinary.com/demo/image/upload/{Transformation}/v1700000000/p/abc.jpg")]
    [InlineData(
        "https://res.cloudinary.com/demo/image/upload/v1/p/abc.jpg",
        $"https://res.cloudinary.com/demo/image/upload/{Transformation}/v1/p/abc.jpg")]
    [InlineData(
        "https://res.cloudinary.com/demo/image/upload/v1/p/no-extension",
        $"https://res.cloudinary.com/demo/image/upload/{Transformation}/v1/p/no-extension")]
    public void ForPublishing_CloudinaryImageUrl_IsNormalisedToABoundedJpeg(string url, string expected) =>
        Assert.Equal(expected, SocialImageUrlTransformer.ForPublishing(url));

    [Fact]
    public void ForPublishing_KeepsAnExistingTransformationChain_AfterTheSafetyTransformation() =>
        Assert.Equal(
            $"https://res.cloudinary.com/demo/image/upload/{Transformation}/c_fill,w_800/v1/p/abc.jpg",
            SocialImageUrlTransformer.ForPublishing("https://res.cloudinary.com/demo/image/upload/c_fill,w_800/v1/p/abc.png"));

    [Fact]
    public void ForPublishing_PreservesTheQueryString() =>
        Assert.Equal(
            $"https://res.cloudinary.com/demo/image/upload/{Transformation}/v1/p/abc.jpg?_a=xyz",
            SocialImageUrlTransformer.ForPublishing("https://res.cloudinary.com/demo/image/upload/v1/p/abc.png?_a=xyz"));

    [Fact]
    public void ForPublishing_IsIdempotent()
    {
        var once = SocialImageUrlTransformer.ForPublishing("https://res.cloudinary.com/demo/image/upload/v1/p/abc.png");

        Assert.Equal(once, SocialImageUrlTransformer.ForPublishing(once));
    }

    [Theory]
    [InlineData("https://cdn.example.test/p/abc.png")]
    [InlineData("https://res.cloudinary.com/demo/video/upload/v1/p/clip.mp4")]
    [InlineData("https://res.cloudinary.com/demo/image/fetch/https://other.test/a.png")]
    [InlineData("https://res.cloudinary.com/demo/image/private/v1/p/abc.png")]
    [InlineData("https://evil-cloudinary.com/demo/image/upload/v1/p/abc.png")]
    [InlineData("https://res.cloudinary.com.evil.test/demo/image/upload/v1/p/abc.png")]
    [InlineData("not a url")]
    [InlineData("/relative/image/upload/abc.png")]
    public void ForPublishing_AnythingThatIsNotACloudinaryImageDeliveryUrl_IsReturnedUnchanged(string url) =>
        Assert.Equal(url, SocialImageUrlTransformer.ForPublishing(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForPublishing_NullOrBlank_IsReturnedUnchanged(string? url) =>
        Assert.Equal(url, SocialImageUrlTransformer.ForPublishing(url));
}
