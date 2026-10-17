using HudhudNestApi.Application.Notifications;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Infrastructure.Tests.Notifications;

public sealed class NotificationKindCatalogTests
{
    public static IEnumerable<object[]> AllTypes() =>
        Enum.GetValues<NotificationType>().Select(t => new object[] { t });

    [Theory(DisplayName = "Every NotificationType has an explicit category, priority and target in the catalog")]
    [MemberData(nameof(AllTypes))]
    public void EveryType_HasACatalogRow(NotificationType type)
    {
        Assert.True(
            NotificationKindCatalog.IsDefined(type),
            $"{type} has no row in NotificationKindCatalog: decide its category, priority, target and whether it is mandatory.");

        var kind = NotificationKindCatalog.Describe(type);
        Assert.False(string.IsNullOrWhiteSpace(kind.Category));
        Assert.False(string.IsNullOrWhiteSpace(kind.TargetKind));
    }

    [Fact(DisplayName = "Message notifications open the property thread, never carry the message id as target")]
    public void NewMessage_TargetsThePropertyThread()
    {
        var property = Guid.NewGuid();
        var message = Guid.NewGuid();

        var target = NotificationKindCatalog.TargetFor(NotificationType.NewMessage, property, message);

        Assert.Equal("messages", target.Kind);
        Assert.Equal(property, target.PropertyId);
        Assert.Null(target.Id);
    }

    [Fact(DisplayName = "Visit and service notifications target the related entity")]
    public void RelatedEntityTypes_TargetTheEntity()
    {
        var related = Guid.NewGuid();
        Assert.Equal(related, NotificationKindCatalog.TargetFor(NotificationType.VisitConfirmed, Guid.NewGuid(), related).Id);
        Assert.Equal(related, NotificationKindCatalog.TargetFor(NotificationType.ServiceRequestAccepted, null, related).Id);
        Assert.Equal(related, NotificationKindCatalog.TargetFor(NotificationType.ValuationResultReady, null, related).Id);
    }

    [Fact(DisplayName = "Security-relevant and transactional events are High priority and not optional")]
    public void ImportantEvents_AreHighAndMandatory()
    {
        foreach (var type in new[]
                 {
                     NotificationType.ListingExpired, NotificationType.ListingTakenDown, NotificationType.PhoneVerification,
                     NotificationType.ShortStayBookingConfirmed, NotificationType.ShortStayBookingRejected,
                 })
        {
            var kind = NotificationKindCatalog.Describe(type);
            Assert.Equal(NotificationPriority.High, kind.Priority);
            Assert.True(kind.IsMandatory, type.ToString());
        }

        Assert.Equal(NotificationPriority.Low, NotificationKindCatalog.Describe(NotificationType.UserRated).Priority);
        Assert.False(NotificationKindCatalog.Describe(NotificationType.NewMessage).IsMandatory);
    }
}
