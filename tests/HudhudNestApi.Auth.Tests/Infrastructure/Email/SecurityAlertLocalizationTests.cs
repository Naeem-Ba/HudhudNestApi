using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Infrastructure.Email;

namespace HudhudNestApi.Auth.Tests.Infrastructure.Email;

/// <summary>Security emails are written in the account owner's language; unknown or unchosen stays English.</summary>
public sealed class SecurityAlertLocalizationTests
{
    private static (SecurityAlertEmailService Service, List<(string Subject, string Body)> Sent) Create(
        Func<string, string?>? language)
    {
        var sent = new List<(string, string)>();
        var sender = new Mock<IEmailSender>();
        sender.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, subject, body) => sent.Add((subject, body)))
            .Returns(Task.CompletedTask);

        ISecurityAlertLanguageResolver? resolver = null;
        if (language is not null)
        {
            var mock = new Mock<ISecurityAlertLanguageResolver>();
            mock.Setup(r => r.GetLanguageByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string email, CancellationToken _) => language(email));
            resolver = mock.Object;
        }

        return (new SecurityAlertEmailService(sender.Object, NullLogger<SecurityAlertEmailService>.Instance, resolver), sent);
    }

    [Fact]
    public async Task AGermanAccount_GetsAGermanEmail_AnArabicAccountAnArabicOne_InTheSameProcess()
    {
        var (service, sent) = Create(email => email.StartsWith("de") ? "de" : "ar");

        await service.SendPasswordChangedConfirmationAsync("de@example.com", "Anna");
        await service.SendPasswordChangedConfirmationAsync("ar@example.com", "ليلى");

        Assert.Equal("✅ Passwort erfolgreich geändert", sent[0].Subject);
        Assert.Contains("Hallo Anna", sent[0].Body);
        Assert.Equal("✅ تم تغيير كلمة المرور بنجاح", sent[1].Subject);
        Assert.Contains("مرحبًا ليلى", sent[1].Body);
    }

    [Fact]
    public async Task AnAccountThatNeverChose_OrWithNoResolver_KeepsTheEnglishWording()
    {
        var (withNull, sentNull) = Create(_ => null);
        var (withoutResolver, sentNone) = Create(null);

        await withNull.SendAccountLockedAlertAsync("u@example.com", "U", DateTime.UtcNow.AddMinutes(15), "1.2.3.4");
        await withoutResolver.SendAccountLockedAlertAsync("u@example.com", "U", DateTime.UtcNow.AddMinutes(15), "1.2.3.4");

        Assert.Equal("🔴 CRITICAL: Your Account Has Been Locked", sentNull.Single().Subject);
        Assert.Equal("🔴 CRITICAL: Your Account Has Been Locked", sentNone.Single().Subject);
    }

    [Fact]
    public async Task ALookupFailure_FallsBackToEnglish_AndStillSendsTheAlert()
    {
        var (service, sent) = Create(_ => throw new InvalidOperationException("db down"));

        await service.SendPasswordChangedConfirmationAsync("u@example.com", "U");

        Assert.Equal("✅ Password Changed Successfully", sent.Single().Subject);
    }

    [Theory]
    [InlineData("ar", "security@hudhudnest.com")]
    [InlineData("de", "security@hudhudnest.com")]
    public async Task PasswordChangeRequest_InEveryLanguage_KeepsTheLinkAndTheSecurityContact(string lang, string contact)
    {
        var (service, sent) = Create(_ => lang);

        await service.SendPasswordChangeRequestAsync("u@example.com", "U", "https://hudhudnest.com/reset?x=1", 5, "1.2.3.4");

        var (_, body) = sent.Single();
        Assert.Contains("https://hudhudnest.com/reset?x=1", body);
        Assert.Contains(contact, body);
        Assert.Contains("1.2.3.4", body);
        Assert.DoesNotContain("Failed Attempts", body);
    }

    [Theory]
    [InlineData("ar", 3, "⚠️ تنبيه أمني: 3 محاولات دخول فاشلة إلى حسابك")]
    [InlineData("de", 4, "🔴 Wichtig: 4 fehlgeschlagene Anmeldeversuche - Ihr Konto wird bald gesperrt")]
    [InlineData("en", 3, "⚠️ Security Alert: 3 Failed Login Attempts to Your Account")]
    public async Task FailedLogin_SubjectFollowsTheAccountLanguageAndAttemptCount(string lang, int attempts, string subject)
    {
        var (service, sent) = Create(_ => lang);

        await service.SendFailedLoginAttemptAlertAsync("u@example.com", "U", attempts, null, DateTime.UtcNow);

        Assert.Equal(subject, sent.Single().Subject);
    }

    [Fact]
    public void EveryTemplate_InEveryLanguage_IsNonEmpty_AndNamesTheUser()
    {
        foreach (var lang in new[] { "ar", "en", "de" })
        {
            var all = new[]
            {
                SecurityAlertTemplates.FailedLogin(lang, "Sam", 3, "1.1.1.1", DateTime.UtcNow),
                SecurityAlertTemplates.FailedLogin(lang, "Sam", 4, null, DateTime.UtcNow),
                SecurityAlertTemplates.FailedLogin(lang, "Sam", 1, null, DateTime.UtcNow),
                SecurityAlertTemplates.PasswordChangeRequest(lang, "Sam", "https://x", 3, null),
                SecurityAlertTemplates.AccountLocked(lang, "Sam", null, null, 15),
                SecurityAlertTemplates.AccountLocked(lang, "Sam", DateTime.UtcNow.AddMinutes(15), "1.1.1.1", 15),
                SecurityAlertTemplates.PasswordChanged(lang, "Sam", DateTime.UtcNow),
            };

            foreach (var (subject, body) in all)
            {
                Assert.False(string.IsNullOrWhiteSpace(subject));
                Assert.Contains("Sam", body);
                Assert.DoesNotContain("{", body);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fr")]
    [InlineData("")]
    public void AnUnsupportedLanguage_ResolvesToEnglish(string? language)
    {
        Assert.Equal("en", SecurityAlertTemplates.Resolve(language));
    }
}
