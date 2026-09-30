using HudhudNestApi.Application.SocialDistribution.AiContent;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution.AiContent;

/// <summary>Phase 8 spec §31.A — the deterministic template generator, and the fact validator it must always pass.</summary>
public sealed class TemplateSocialContentGeneratorTests
{
    private static PropertySocialFacts MakeFacts(decimal? price = 75000m, decimal? area = 120m, int? rooms = 3) => new(
        PropertyId: Guid.NewGuid(),
        Title: "شقة رائعة للبيع في طرطوس",
        PropertyType: "شقة",
        TransactionType: "ForSale",
        Province: "طرطوس",
        City: "طرطوس",
        Address: null,
        Price: price,
        Currency: "USD",
        Area: area,
        Rooms: rooms,
        Bathrooms: null,
        Status: "Available",
        CanonicalUrl: "https://realestateworld.world/properties/p1?utm_source=facebook&utm_medium=social&utm_campaign=social_distribution");

    private readonly TemplateSocialContentGenerator _generator = new();

    [Fact]
    public async Task GenerateAsync_NeverChangesTitle_FromSuppliedFacts()
    {
        var facts = MakeFacts();
        var result = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, SocialPlatform.Facebook, "ar"));

        Assert.Equal(facts.Title, result.Title);
    }

    [Fact]
    public async Task GenerateAsync_IncludesRealPriceAndArea_InBody()
    {
        var facts = MakeFacts(price: 75000m, area: 120m);
        var result = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, SocialPlatform.Facebook, "ar"));

        Assert.Contains("75,000", result.Body);
        Assert.Contains("120", result.Body);
    }

    [Fact]
    public async Task GenerateAsync_IncludesCanonicalUrl_NeverAnInventedLink()
    {
        var facts = MakeFacts();
        var result = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, SocialPlatform.Telegram, "ar"));

        Assert.Contains(facts.CanonicalUrl, result.Body);
    }

    [Fact]
    public async Task GenerateAsync_NeverRequiresReview_ForFactDerivedContent()
    {
        var facts = MakeFacts();
        var result = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, SocialPlatform.Instagram, "ar"));

        Assert.False(result.RequiresReview);
    }

    [Fact]
    public async Task GenerateAsync_EnglishLanguage_ProducesEnglishCopy()
    {
        var facts = MakeFacts();
        var result = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, SocialPlatform.Facebook, "en"));

        Assert.Contains("for sale", result.Body);
    }

    [Fact]
    public async Task GenerateAsync_OutputAlwaysPassesTheFactValidator()
    {
        var facts = MakeFacts();
        foreach (var platform in new[] { SocialPlatform.Facebook, SocialPlatform.Instagram, SocialPlatform.Telegram })
        {
            var result = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, platform, "ar"));
            var validation = SocialContentFactValidator.Validate(result, facts);

            Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        }
    }

    [Fact]
    public async Task GenerateAsync_SameFactsTwice_ProducesSameSourceFactsHash()
    {
        var facts = MakeFacts();
        var first = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, SocialPlatform.Facebook, "ar"));
        var second = await _generator.GenerateAsync(new GenerateSocialContentRequest(facts, SocialPlatform.Facebook, "ar"));

        Assert.Equal(first.SourceFactsHash, second.SourceFactsHash);
    }

    [Fact]
    public async Task GenerateAsync_DifferentPrice_ProducesDifferentSourceFactsHash()
    {
        var first = await _generator.GenerateAsync(new GenerateSocialContentRequest(MakeFacts(price: 75000m), SocialPlatform.Facebook, "ar"));
        var second = await _generator.GenerateAsync(new GenerateSocialContentRequest(MakeFacts(price: 80000m), SocialPlatform.Facebook, "ar"));

        Assert.NotEqual(first.SourceFactsHash, second.SourceFactsHash);
    }
}

/// <summary>Phase 8 spec §31.A — independently exercising the validator with fabricated (not generator-produced) content, since the shipped generator is safe by construction.</summary>
public sealed class SocialContentFactValidatorTests
{
    private static PropertySocialFacts MakeFacts() => new(
        Guid.NewGuid(), "شقة للبيع", "شقة", "ForSale", "طرطوس", "طرطوس", null,
        75000m, "USD", 120m, 3, null, "Available", "https://realestateworld.world/properties/p1");

    private static GeneratedSocialContent MakeContent(string body, SocialPlatform platform = SocialPlatform.Facebook) => new(
        platform, "شقة للبيع", body, null, Array.Empty<string>(), null, "ar", "hash", false);

    [Fact]
    public void Validate_FabricatedPrice_IsRejected()
    {
        var facts = MakeFacts();
        var content = MakeContent("شقة رائعة بسعر 999999 دولار فقط!");

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RealPrice_IsAccepted()
    {
        var facts = MakeFacts();
        var content = MakeContent("شقة رائعة بسعر 75000 دولار فقط!");

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownUrl_IsRejected()
    {
        var facts = MakeFacts();
        var content = MakeContent("للتفاصيل زوروا https://phishing.example.com/fake");

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_CanonicalUrl_IsAccepted()
    {
        var facts = MakeFacts();
        var content = MakeContent($"للتفاصيل زوروا {facts.CanonicalUrl}");

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_HtmlMarkup_IsRejected()
    {
        var facts = MakeFacts();
        var content = MakeContent("<script>alert(1)</script> شقة رائعة");

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_BodyExceedingPlatformLimit_IsRejected()
    {
        var facts = MakeFacts();
        var content = MakeContent(new string('a', 6000), SocialPlatform.Facebook);

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.False(result.IsValid);
    }

    // A real property link carries a GUID id and UTM params — digits inside the link are not
    // "figures the generator invented", so they must never be mistaken for a fabricated price.
    // (Regression: Facebook/Telegram bodies, which include the link line, were rejected for any
    // GUID containing 4+ consecutive digits and silently fell back to the raw default body.)
    private const string RealisticCanonicalUrl =
        "https://realestateworld.world/properties/979a8254-fff2-4d78-bf31-b4331ddea0ad" +
        "?utm_source=facebook&utm_medium=social&utm_campaign=social_distribution&utm_content=publication_5d642dce-5ab1-40fe-92bf-b5421ee70064";

    [Theory]
    [InlineData(SocialPlatform.Facebook)]
    [InlineData(SocialPlatform.Telegram)]
    [InlineData(SocialPlatform.Instagram)]
    public async Task Validate_GeneratedContent_WithGuidAndUtmDigitsInTheLink_IsAccepted(SocialPlatform platform)
    {
        var facts = MakeFacts() with { Price = 500m, CanonicalUrl = RealisticCanonicalUrl };
        var content = await new TemplateSocialContentGenerator().GenerateAsync(new GenerateSocialContentRequest(facts, platform, "ar"));

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Validate_FabricatedPrice_NextToARealisticLink_IsStillRejected()
    {
        var facts = MakeFacts() with { Price = 500m, CanonicalUrl = RealisticCanonicalUrl };
        var content = MakeContent($"بسعر 999999 دولار فقط {facts.CanonicalUrl}");

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NoPriceInFacts_NeverFlagsUnrelatedNumbers()
    {
        var facts = MakeFacts() with { Price = null };
        var content = MakeContent("شقة فيها 3 غرف و2 حمام قرب الجامعة");

        var result = SocialContentFactValidator.Validate(content, facts);

        Assert.True(result.IsValid);
    }
}
