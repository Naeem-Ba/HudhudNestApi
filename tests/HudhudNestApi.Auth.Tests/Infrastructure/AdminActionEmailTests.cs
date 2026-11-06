using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Infrastructure.Admin;
using HudhudNestApi.Infrastructure.Email;
using CloudinaryDotNet;
using HudhudNestApi.Infrastructure.Media;

namespace HudhudNestApi.Auth.Tests.Infrastructure;

/// <summary>QA phase 17: P17-D07 admin action notices, P17-D09 mail encoding, P17-D02 metadata stripping.</summary>
[Trait("Category", "Security")]
public sealed class AdminActionEmailTests
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryLanguageAndAudience_NamesTheActorAndTheAction(string lang)
    {
        foreach (var action in Enum.GetValues<AdminAccountAction>())
        {
            var owner = AdminActionEmailNotifier.Compose(lang, action, forOwner: true, "a***@x.test", "t***@x.test", null);
            var admin = AdminActionEmailNotifier.Compose(lang, action, forOwner: false, "boss@x.test", "victim@x.test", "roles: Admin");
            Assert.False(string.IsNullOrWhiteSpace(owner.Subject));
            Assert.Contains("a***@x.test", owner.Body);
            Assert.Contains("boss@x.test", admin.Body);
            Assert.Contains("victim@x.test", admin.Subject);
        }
    }

    [Fact]
    public void TheDetailIsHtmlEncoded()
    {
        var (_, body) = AdminActionEmailNotifier.Compose("en", AdminAccountAction.RoleChanged, true, "a", "b", "<script>x</script>");
        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);
    }

    [Fact]
    public void UnknownLanguage_FallsBackToArabic()
    {
        var (subject, _) = AdminActionEmailNotifier.Compose("fr", AdminAccountAction.Disabled, true, "a", "b", null);
        Assert.Contains("تنبيه أمني", subject);
    }

    [Fact]
    public void SecurityAlertNames_AreHtmlEncoded()
    {
        Assert.Equal("&lt;img src=x onerror=alert(1)&gt;", SecurityAlertEmailService.SafeName("<img src=x onerror=alert(1)>"));
    }

    [Fact]
    public void Uploads_ClearExifGpsAndOtherMetadata()
    {
        var flags = CloudinaryMediaStorageService.StripMetadataTransformation().Generate();
        Assert.Contains("fl_force_strip", flags);
    }
}
