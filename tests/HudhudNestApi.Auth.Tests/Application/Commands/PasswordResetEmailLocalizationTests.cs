using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Auth.Commands.ForgotPassword;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Auth.Tests.Application.Commands;

/// <summary>QA Phase 16 D02: the password-reset email was English only and called the product "HudhudNestApi".</summary>
[Trait("Category", "AuthCQRS")]
public sealed class PasswordResetEmailLocalizationTests
{
    private const string Url = "https://app.example.com/reset-password?email=a%40b.c&token=t";

    [Theory]
    [InlineData("ar", "rtl", "إعادة تعيين كلمة المرور")]
    [InlineData("de", "ltr", "Passwort zurücksetzen")]
    [InlineData("en", "ltr", "Reset your password")]
    public void Html_IsWrittenInTheLanguage_WithItsDirection_AndTheRealProductName(string lang, string dir, string linkText)
    {
        var html = PasswordResetEmailTemplate.BuildHtml(lang, "Naeem", Url);

        Assert.Contains($"lang=\"{lang}\"", html);
        Assert.Contains($"dir=\"{dir}\"", html);
        Assert.Contains(linkText, html);
        Assert.Contains("Naeem", html);
        Assert.Contains(Url.Replace("&", "&amp;"), html);
        Assert.DoesNotContain("HudhudNestApi", html);
        Assert.Contains(lang == "ar" ? "هدهد نيست" : "HudhudNest", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public void UnknownLanguage_FallsBackToArabic_TheDefaultDisplayLanguage(string? lang)
    {
        Assert.Equal("إعادة تعيين كلمة المرور في هدهد نيست", PasswordResetEmailTemplate.SubjectFor(lang));
        Assert.Contains("lang=\"ar\"", PasswordResetEmailTemplate.BuildHtml(lang, null, Url));
    }

    [Fact]
    public void Subjects_AreTranslated()
    {
        Assert.Equal("HudhudNest: Passwort zurücksetzen", PasswordResetEmailTemplate.SubjectFor("de-AT"));
        Assert.Equal("إعادة تعيين كلمة المرور في هدهد نيست", PasswordResetEmailTemplate.SubjectFor("AR"));
    }

    [Fact]
    public void DisplayName_IsHtmlEncoded()
        => Assert.DoesNotContain("<script>", PasswordResetEmailTemplate.BuildHtml("en", "<script>x</script>", Url));

    private static (ForgotPasswordCommandHandler Handler, Mock<IApplicationEmailSender> Sender) Build(string? accountLanguage)
    {
        var id = Guid.NewGuid();
        var identity = new Mock<IForgotPasswordIdentityService>();
        identity.Setup(x => x.FindByEmailAsync("naeem@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountSnapshot(id, id, "naeem@example.com", null, true, false, true, false, "naeem@example.com", "stamp"));
        identity.Setup(x => x.GeneratePasswordResetTokenAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync("t");

        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        var url = new Mock<IPasswordResetUrlBuilder>();
        url.Setup(x => x.Build(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Url);

        var resolver = new Mock<ISecurityAlertLanguageResolver>();
        resolver.Setup(x => x.GetLanguageByEmailAsync("naeem@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(accountLanguage);

        var sender = new Mock<IApplicationEmailSender>();
        return (new ForgotPasswordCommandHandler(identity.Object, accounts.Object, sender.Object, url.Object,
            NullLogger<ForgotPasswordCommandHandler>.Instance, resolver.Object), sender);
    }

    [Theory]
    [InlineData("de", "en", "HudhudNest: Passwort zurücksetzen")]   // the account's own choice wins
    [InlineData(null, "de", "HudhudNest: Passwort zurücksetzen")]    // else the site language of the request
    [InlineData(null, null, "إعادة تعيين كلمة المرور في هدهد نيست")] // else Arabic, the default display language
    public async Task Handler_PicksTheLanguage_AccountFirst_ThenRequest_ThenArabic(string? account, string? request, string subject)
    {
        var (handler, sender) = Build(account);

        var result = await handler.Handle(new ForgotPasswordCommand("naeem@example.com", "https", "app.example.com", request), CancellationToken.None);

        Assert.True(result.Success);
        sender.Verify(x => x.SendEmailAsync("naeem@example.com", subject, It.IsAny<string>()), Times.Once);
    }
}
