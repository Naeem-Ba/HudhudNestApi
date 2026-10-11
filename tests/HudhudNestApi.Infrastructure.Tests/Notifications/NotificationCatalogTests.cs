using System.Text.RegularExpressions;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Infrastructure.Notifications.Localization;

namespace HudhudNestApi.Infrastructure.Tests.Notifications;

public sealed class NotificationCatalogTests
{
    private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static IEnumerable<object[]> AllKeys() =>
        NotificationCatalog.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void EveryKey_HasANonBlankTranslation_InEveryLanguage(string key)
    {
        foreach (var lang in new[] { "ar", "en", "de" })
        {
            Assert.True(NotificationCatalog.TryGetTemplate(key, lang, out var template));
            Assert.False(string.IsNullOrWhiteSpace(template), $"{key}/{lang} is blank");
        }
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void EveryTranslation_UsesTheSamePlaceholders_AsTheOthers(string key)
    {
        NotificationCatalog.TryGetTemplate(key, "ar", out var ar);
        NotificationCatalog.TryGetTemplate(key, "en", out var en);
        NotificationCatalog.TryGetTemplate(key, "de", out var de);

        var expected = Names(ar);
        Assert.Equal(expected, Names(en));
        Assert.Equal(expected, Names(de));
    }

    [Fact]
    public void Render_FillsPlaceholders_InTheRequestedLanguage()
    {
        var text = NotificationText.Of("msg.property_published", ("title", "Villa"));

        Assert.Equal("تم نشر العقار 'Villa' بنجاح.", NotificationCatalog.Render(text, "ar"));
        Assert.Equal("The property 'Villa' was published successfully.", NotificationCatalog.Render(text, "en"));
        Assert.Equal("Die Immobilie 'Villa' wurde erfolgreich veröffentlicht.", NotificationCatalog.Render(text, "de"));
    }

    [Fact]
    public void UserSuppliedText_IsNeverReExpandedAsAPlaceholder()
    {
        var text = NotificationText.Of("msg.new_message", ("sender", "{sender}{title}"));

        Assert.Equal("{sender}{title} sent you a new message.", NotificationCatalog.Render(text, "en"));
    }

    [Fact]
    public void AnUnsupportedLanguage_FallsBackToArabic_AndNeverShowsARawKey()
    {
        var text = NotificationText.Of("msg.valuation_result_ready");

        var rendered = NotificationCatalog.Render(text, "fr");

        Assert.StartsWith("اكتمل تقييم عقارك", rendered);
        Assert.DoesNotContain("msg.", rendered);
    }

    [Fact]
    public void AnUnknownKey_RendersNothingTechnical()
    {
        Assert.Equal(string.Empty, NotificationCatalog.Render(NotificationText.Of("no.such.key"), "en"));
    }

    [Fact]
    public void ALiteral_IsShownAsIs_InEveryLanguage()
    {
        var text = NotificationText.FromLiteral("نص جاهز");

        Assert.Equal("نص جاهز", NotificationCatalog.Render(text, "de"));
    }

    [Fact]
    public void DatesAndNumbers_AreFormattedInvariantly()
    {
        var text = NotificationText.Of(
            "visit.detail.cancelled",
            ("name", "Layla"),
            ("at", new DateTime(2027, 3, 4, 9, 5, 0)));

        Assert.Equal("Layla — cancelled time: 04/03/2027 09:05.", NotificationCatalog.Render(text, "en"));
    }

    private static string[] Names(string template) =>
        Placeholder.Matches(template).Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
}
