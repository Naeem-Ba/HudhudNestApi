using HudhudNestApi.Application.Notifications;
using HudhudNestApi.Application.Notifications.Preferences;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Tests.Notifications;

public sealed class NotificationDeliveryPolicyTests
{
    private static readonly NotificationKind Message = NotificationKindCatalog.Describe(NotificationType.NewMessage);
    private static readonly NotificationKind Expiry = NotificationKindCatalog.Describe(NotificationType.ListingExpired);
    private static readonly NotificationKind SavedSearch = NotificationKindCatalog.Describe(NotificationType.SavedSearchMatch);
    private static readonly DateTime Noon = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutPreferences_EverythingIsStored_AndNameBearingPushIsGeneric()
    {
        Assert.True(NotificationDeliveryPolicy.ShouldStore(null, Message));
        Assert.Equal(PushDecision.Generic, NotificationDeliveryPolicy.DecidePush(null, Message, Noon));
        Assert.Equal(PushDecision.Full, NotificationDeliveryPolicy.DecidePush(null, SavedSearch, Noon));
    }

    [Fact]
    public void FullPreview_SendsTheFullText()
    {
        var prefs = new NotificationPreference { PushPreview = PushPreviewLevel.Full };
        Assert.Equal(PushDecision.Full, NotificationDeliveryPolicy.DecidePush(prefs, Message, Noon));
    }

    [Fact]
    public void MutedCategory_IsNotStored_ButMandatoryEventsAlwaysAre()
    {
        var prefs = new NotificationPreference { MutedCategories = { "messages", "listings" } };

        Assert.False(NotificationDeliveryPolicy.ShouldStore(prefs, Message));
        Assert.True(NotificationDeliveryPolicy.ShouldStore(prefs, Expiry)); // mandatory, same category as a muted one
    }

    [Fact]
    public void PushMutedCategory_StillStores_ButNeverRings_UnlessMandatory()
    {
        var prefs = new NotificationPreference { PushMutedCategories = { "messages", "listings" } };

        Assert.True(NotificationDeliveryPolicy.ShouldStore(prefs, Message));
        Assert.Equal(PushDecision.Skip, NotificationDeliveryPolicy.DecidePush(prefs, Message, Noon));
        Assert.NotEqual(PushDecision.Skip, NotificationDeliveryPolicy.DecidePush(prefs, Expiry, Noon));
    }

    [Theory]
    [InlineData(23, 0, true)]   // 23:00 inside 22:00-07:00
    [InlineData(3, 30, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]   // end is exclusive
    [InlineData(12, 0, false)]
    [InlineData(21, 59, false)]
    [InlineData(22, 0, true)]   // start is inclusive
    public void QuietHours_SpanningMidnight_UseTheUsersZone(int hour, int minute, bool quiet)
    {
        // Damascus is UTC+3 in October 2026 (no DST), so local hour h is UTC hour h-3.
        var prefs = new NotificationPreference { QuietStartMinute = 22 * 60, QuietEndMinute = 7 * 60, TimeZoneId = "Asia/Damascus" };
        var utc = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc).AddHours(hour - 3).AddMinutes(minute);

        Assert.Equal(quiet, NotificationDeliveryPolicy.InQuietHours(prefs, utc));
    }

    [Fact]
    public void QuietHours_HoldBackNormalPush_ButNotHighPriority()
    {
        var prefs = new NotificationPreference { QuietStartMinute = 0, QuietEndMinute = 23 * 60 }; // UTC, almost all day
        var night = new DateTime(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(PushDecision.Skip, NotificationDeliveryPolicy.DecidePush(prefs, Message, night));
        Assert.NotEqual(PushDecision.Skip, NotificationDeliveryPolicy.DecidePush(prefs, Expiry, night)); // High + mandatory
        Assert.False(NotificationDeliveryPolicy.ShouldStore(new NotificationPreference { MutedCategories = { "messages" } }, Message));
        Assert.True(NotificationDeliveryPolicy.ShouldStore(prefs, Message)); // quiet hours never stop the in-app copy
    }

    [Fact]
    public void UnknownTimeZone_IsReadAsUtc()
    {
        var prefs = new NotificationPreference { QuietStartMinute = 60, QuietEndMinute = 120, TimeZoneId = "Nowhere/Land" };
        Assert.True(NotificationDeliveryPolicy.InQuietHours(prefs, new DateTime(2026, 10, 17, 1, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void OffOrDegenerateQuietHours_NeverApply()
    {
        Assert.False(NotificationDeliveryPolicy.InQuietHours(new NotificationPreference(), Noon));
        Assert.False(NotificationDeliveryPolicy.InQuietHours(new NotificationPreference { QuietStartMinute = 60, QuietEndMinute = 60 }, Noon));
    }
}
