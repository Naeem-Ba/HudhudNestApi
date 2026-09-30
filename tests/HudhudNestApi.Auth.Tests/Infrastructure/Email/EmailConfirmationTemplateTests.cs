using HudhudNestApi.Infrastructure.Email.Templates;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Email;

/// <summary>
/// The bilingual confirmation message.
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

    [Fact]
    public void Subject_CarriesBothLanguages()
    {
        Assert.Contains("تأكيد بريدك الإلكتروني", EmailConfirmationTemplate.Subject, StringComparison.Ordinal);
        Assert.Contains("Confirm your email", EmailConfirmationTemplate.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain('?', EmailConfirmationTemplate.Subject);
    }

    [Fact]
    public void Html_CarriesBothLanguages_AndMarksTheArabicBlockRightToLeft()
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url);

        Assert.Contains("تأكيد البريد الإلكتروني", html, StringComparison.Ordinal);
        Assert.Contains("Confirm email", html, StringComparison.Ordinal);
        Assert.Contains("dir=\"rtl\"", html, StringComparison.Ordinal);
        Assert.Contains("dir=\"ltr\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_EncodesTheLinkForAnAttribute()
    {
        var html = EmailConfirmationTemplate.BuildHtml(Url);

        // The raw ampersand between the two query parameters would otherwise sit unescaped
        // inside an href.
        Assert.Contains("userId=abc&amp;token=xyz", html, StringComparison.Ordinal);
        Assert.DoesNotContain("userId=abc&token=xyz", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_CarriesBothLanguages_AndTheBareLink()
    {
        var text = EmailConfirmationTemplate.BuildText(Url);

        Assert.Contains("تأكيد بريدك الإلكتروني", text, StringComparison.Ordinal);
        Assert.Contains("Confirm your email", text, StringComparison.Ordinal);

        // Unescaped here: a plain-text part is not markup, and an &amp; in a pasted URL
        // is a broken URL.
        Assert.Contains(Url, text, StringComparison.Ordinal);
    }
}
