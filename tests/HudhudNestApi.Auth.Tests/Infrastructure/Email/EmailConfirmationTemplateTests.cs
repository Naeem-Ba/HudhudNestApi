using HudhudNestApi.Infrastructure.Email.Templates;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Email;

/// <summary>
/// The confirmation message, in the reader's language or, when that is unknown, in Arabic
/// and English together.
///
/// The encoding assertions are not paranoia: PhoneAuthController.cs in this same solution
/// was saved through a non-UTF-8 editor at some point and its Arabic response strings are
/// now literal question marks, shipped to users. A source file that loses its Arabic still
/// compiles, so only an assertion catches it.
/// </summary>
public sealed class EmailConfirmationTemplateTests
{
    private const string Url =
        "https://app.example.com/auth/verify-email?userId=abc&token=xyz";

    private const string ArabicHeading = "أكِّد بريدك الإلكتروني";
    private const string EnglishHeading = "Confirm your email address";
    private const string GermanHeading = "E-Mail-Adresse bestätigen";

    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("AR", "ar")]
    [InlineData("ar-SA", "ar")]
    [InlineData("en", "en")]
    [InlineData("en_US", "en")]
    [InlineData("  De-at ", "de")]
    [InlineData("fr", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("x-klingon", null)]
    public void NormalizeLanguage_AcceptsTheShippedLanguages_AndNothingElse(string? input, string? expected)
        => Assert.Equal(expected, EmailConfirmationTemplate.NormalizeLanguage(input));

    [Fact]
    public void Subject_WithoutALanguage_NamesThePlatformAndCarriesBothLanguages()
    {
        var subject = EmailConfirmationTemplate.SubjectFor(null);

        Assert.Equal(EmailConfirmationTemplate.Subject, subject);
        Assert.Contains("هدهد نيست", subject, StringComparison.Ordinal);
        Assert.Contains("HudhudNest", subject, StringComparison.Ordinal);
        Assert.Contains("أكِّد بريدك الإلكتروني", subject, StringComparison.Ordinal);
        Assert.Contains("Confirm your email", subject, StringComparison.Ordinal);
        Assert.DoesNotContain('?', subject);
    }

    [Theory]
    [InlineData("ar", "هدهد نيست")]
    [InlineData("en", "HudhudNest")]
    [InlineData("de", "HudhudNest")]
    public void Subject_InAShippedLanguage_NamesThePlatform(string language, string brand)
    {
        var subject = EmailConfirmationTemplate.SubjectFor(language);

        Assert.Contains(brand, subject, StringComparison.Ordinal);
        Assert.DoesNotContain('?', subject);
        Assert.NotEqual(EmailConfirmationTemplate.Subject, subject);
    }

    [Fact]
    public void Html_WithoutALanguage_CarriesBothLanguages_AndMarksEachBlockItsDirection()
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url);

        Assert.Contains(ArabicHeading, html, StringComparison.Ordinal);
        Assert.Contains(EnglishHeading, html, StringComparison.Ordinal);
        Assert.Contains("dir=\"rtl\" lang=\"ar\"", html, StringComparison.Ordinal);
        Assert.Contains("dir=\"ltr\" lang=\"en\"", html, StringComparison.Ordinal);
        Assert.Contains("هدهد نيست · HudhudNest", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_Arabic_IsOnlyArabic_RightToLeft_AndNamesThePlatform()
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url, "ar-SA");

        Assert.Contains(ArabicHeading, html, StringComparison.Ordinal);
        Assert.Contains("تأكيد البريد الإلكتروني", html, StringComparison.Ordinal);
        Assert.Contains("منصة هدهد نيست", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain(EnglishHeading, html, StringComparison.Ordinal);
        Assert.DoesNotContain(GermanHeading, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Confirm email", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_English_IsOnlyEnglish_LeftToRight_AndNamesThePlatform()
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url, "en");

        Assert.Contains(EnglishHeading, html, StringComparison.Ordinal);
        Assert.Contains("Thank you for joining HudhudNest", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain(ArabicHeading, html, StringComparison.Ordinal);
        Assert.DoesNotContain("تأكيد", html, StringComparison.Ordinal);
        Assert.DoesNotContain(GermanHeading, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_German_IsOnlyGerman_AndUsesTheFormalAddress()
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url, "de");

        Assert.Contains(GermanHeading, html, StringComparison.Ordinal);
        Assert.Contains("Willkommen bei HudhudNest", html, StringComparison.Ordinal);
        Assert.Contains("Ihre E-Mail-Adresse", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"de\" dir=\"ltr\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain(ArabicHeading, html, StringComparison.Ordinal);
        Assert.DoesNotContain(EnglishHeading, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_AnUnshippedLanguage_FallsBackToTheBilingualMessage()
    {
        Assert.Equal(
            EmailConfirmationTemplate.BuildHtml(Url),
            EmailConfirmationTemplate.BuildHtml(Url, "fr"));
        Assert.Equal(
            EmailConfirmationTemplate.BuildText(Url),
            EmailConfirmationTemplate.BuildText(Url, "fr"));
        Assert.Equal(
            EmailConfirmationTemplate.Subject,
            EmailConfirmationTemplate.SubjectFor("fr"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ar")]
    [InlineData("en")]
    [InlineData("de")]
    public void Html_EncodesTheLinkForAnAttribute_InEveryLanguage(string? language)
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url, language);

        // The raw ampersand between the two query parameters would otherwise sit unescaped
        // inside an href.
        Assert.Contains("userId=abc&amp;token=xyz", html, StringComparison.Ordinal);
        Assert.DoesNotContain("userId=abc&token=xyz", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ar")]
    [InlineData("en")]
    [InlineData("de")]
    public void Html_ShowsTheLinkAsTextToo_ForClientsThatStripButtons(string? language)
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url, language);

        // Once as the button's href, once as the visible fallback -- per language block, so
        // the bilingual message carries it twice over.
        var blocks = language is null ? 2 : 1;
        Assert.Equal(2 * blocks, CountOccurrences(html, "userId=abc&amp;token=xyz"));
    }

    [Theory]
    [InlineData(null, "Confirm your email address")]
    [InlineData("ar", "أكِّد بريدك الإلكتروني")]
    [InlineData("en", "Confirm your email address")]
    [InlineData("de", "E-Mail-Adresse bestätigen")]
    public void Text_CarriesTheHeading_AndTheBareLink(string? language, string heading)
    {
        var text = EmailConfirmationTemplate.BuildText(Url, language);

        Assert.Contains(heading, text, StringComparison.Ordinal);

        // Unescaped here: a plain-text part is not markup, and an &amp; in a pasted URL
        // is a broken URL.
        Assert.Contains(Url, text, StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_InOneLanguage_DoesNotCarryTheOthers()
    {
        var text = EmailConfirmationTemplate.BuildText(Url, "en");

        Assert.DoesNotContain("هدهد", text, StringComparison.Ordinal);
        Assert.DoesNotContain(GermanHeading, text, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
