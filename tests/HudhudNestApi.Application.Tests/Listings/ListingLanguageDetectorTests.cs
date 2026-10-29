using HudhudNestApi.Application.Listings.Translations;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

public sealed class ListingLanguageDetectorTests
{
    [Theory(DisplayName = "Listing text is classified by script and listing vocabulary")]
    [InlineData("شقة مفروشة في المزة", "ar")]
    [InlineData("شقة للبيع في Damascus ", "ar")]
    [InlineData("Furnished apartment for rent in Mazzeh", "en")]
    [InlineData("Spacious villa with garden and a quiet view", "en")]
    [InlineData("Möblierte Wohnung zu vermieten", "de")]
    [InlineData("Helle Wohnung mit Balkon und Aufzug", "de")]
    [InlineData("Straße", "de")]
    [InlineData("VIP Tower", "latin")]
    public void Detects(string text, string expected) => Assert.Equal(expected, ListingLanguageDetector.Detect(text));

    [Theory(DisplayName = "Mixed, non-letter, empty or other-script text is not classified")]
    [InlineData("شقة apartment شقة apartment")]
    [InlineData("12345 / 678")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Квартира в центре")]
    public void Unclassifiable(string text) => Assert.Null(ListingLanguageDetector.Detect(text));

    [Fact(DisplayName = "The fingerprint is exact: any character or diacritic changes it")]
    public void FingerprintIsExact()
    {
        Assert.Equal(ListingTextFingerprint.Compute("شقة"), ListingTextFingerprint.Compute("شقة"));
        Assert.NotEqual(ListingTextFingerprint.Compute("شقة"), ListingTextFingerprint.Compute("شقّة"));
        Assert.NotEqual(ListingTextFingerprint.Compute("a"), ListingTextFingerprint.Compute("a "));
        Assert.Equal(64, ListingTextFingerprint.Compute("x").Length);
    }
}
