using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Notifications;

/// <summary>What the client needs to open the screen an event belongs to. Never a substitute for authorization:
/// the target page re-checks access with the signed-in account.</summary>
public sealed record NotificationTarget(string Kind, Guid? Id, Guid? PropertyId);

/// <summary>Static facts about one notification type.</summary>
public sealed record NotificationKind(
    string Category,
    NotificationPriority Priority,
    string TargetKind,
    bool IsMandatory);

/// <summary>
/// The single place that says, for every <see cref="NotificationType"/>: its category (used for filters
/// and user preferences), its priority, which screen it opens, and whether the user may turn it off.
/// A test fails when a type has no row, so a new type cannot ship without a decision on each of these.
/// </summary>
public static class NotificationKindCatalog
{
    public const string Messages = "messages";
    public const string Listings = "listings";
    public const string SavedSearch = "saved-search";
    public const string Visits = "visits";
    public const string ShortStay = "short-stay";
    public const string Services = "services";
    public const string Agency = "agency";
    public const string Valuation = "valuation";
    public const string Account = "account";
    public const string Social = "social";

    private static readonly NotificationKind Fallback = new(Account, NotificationPriority.Normal, "notifications", false);

    private static readonly Dictionary<NotificationType, NotificationKind> Kinds = new()
    {
        [NotificationType.NewMessage] = new(Messages, NotificationPriority.Normal, "messages", false),
        [NotificationType.AgencyMemberInquiry] = new(Messages, NotificationPriority.Low, "property", false),

        [NotificationType.PropertyStatusChanged] = new(Listings, NotificationPriority.Normal, "property", false),
        [NotificationType.PropertyPriceChanged] = new(Listings, NotificationPriority.Low, "property", false),
        [NotificationType.PropertyPublished] = new(Listings, NotificationPriority.Normal, "property", false),
        [NotificationType.PropertyUnpublished] = new(Listings, NotificationPriority.Normal, "property", false),
        [NotificationType.ListingExpiringSoon] = new(Listings, NotificationPriority.High, "my-listings", true),
        [NotificationType.ListingExpired] = new(Listings, NotificationPriority.High, "my-listings", true),
        [NotificationType.ListingTakenDown] = new(Listings, NotificationPriority.High, "my-listings", true),
        [NotificationType.ListingRestored] = new(Listings, NotificationPriority.Normal, "my-listings", true),
        [NotificationType.ReviewAdded] = new(Listings, NotificationPriority.Low, "property", false),

        [NotificationType.SavedSearchMatch] = new(SavedSearch, NotificationPriority.Normal, "property", false),

        [NotificationType.VisitRequested] = new(Visits, NotificationPriority.Normal, "visit", false),
        [NotificationType.VisitConfirmed] = new(Visits, NotificationPriority.Normal, "visit", false),
        [NotificationType.VisitDeclined] = new(Visits, NotificationPriority.Normal, "visit", false),
        [NotificationType.VisitCancelled] = new(Visits, NotificationPriority.Normal, "visit", false),
        [NotificationType.VisitRescheduleProposed] = new(Visits, NotificationPriority.Normal, "visit", false),
        [NotificationType.VisitRescheduleAccepted] = new(Visits, NotificationPriority.Normal, "visit", false),
        [NotificationType.VisitRescheduleDeclined] = new(Visits, NotificationPriority.Normal, "visit", false),
        [NotificationType.VisitCancelledByHost] = new(Visits, NotificationPriority.Normal, "visit", false),

        [NotificationType.ShortStayBookingRequested] = new(ShortStay, NotificationPriority.Normal, "short-stay-booking", false),
        [NotificationType.ShortStayBookingApproved] = new(ShortStay, NotificationPriority.High, "short-stay-booking", true),
        [NotificationType.ShortStayBookingRejected] = new(ShortStay, NotificationPriority.High, "short-stay-booking", true),
        [NotificationType.ShortStayBookingConfirmed] = new(ShortStay, NotificationPriority.High, "short-stay-booking", true),
        [NotificationType.ShortStayBookingCancelled] = new(ShortStay, NotificationPriority.Normal, "short-stay-booking", false),
        [NotificationType.ShortStayReviewAdded] = new(ShortStay, NotificationPriority.Low, "short-stay-booking", false),

        [NotificationType.ServiceRequestSubmitted] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceRequestAccepted] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceRequestRejected] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceRequestScheduled] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceRequestCompleted] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceRequestCancelled] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceReviewAdded] = new(Services, NotificationPriority.Low, "service-request", false),
        [NotificationType.ServiceRequestNotCompleted] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceInquiryReceived] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceRequestMessage] = new(Services, NotificationPriority.Normal, "service-request", false),
        [NotificationType.ServiceRequestConfirmReminder] = new(Services, NotificationPriority.Low, "service-request", false),
        [NotificationType.ServiceRequestExpired] = new(Services, NotificationPriority.Low, "service-request", false),
        [NotificationType.ServiceProviderApplicationInfoRequested] = new(Services, NotificationPriority.High, "provider-application", true),
        [NotificationType.ServiceProviderApplicationApproved] = new(Services, NotificationPriority.High, "provider-application", true),
        [NotificationType.ServiceProviderApplicationRejected] = new(Services, NotificationPriority.High, "provider-application", true),
        [NotificationType.ServiceProviderStatusChanged] = new(Services, NotificationPriority.High, "provider-application", true),

        [NotificationType.AgencyInvitationReceived] = new(Agency, NotificationPriority.Normal, "agency-invitation", false),
        [NotificationType.AgencyInvitationAccepted] = new(Agency, NotificationPriority.Normal, "agency", false),
        [NotificationType.AgencyInvitationDeclined] = new(Agency, NotificationPriority.Normal, "agency", false),

        [NotificationType.ValuationInquiryExpired] = new(Valuation, NotificationPriority.Normal, "valuation", false),
        [NotificationType.ValuationOfficeInvitationExpired] = new(Valuation, NotificationPriority.Normal, "valuation", false),
        [NotificationType.ValuationResultReady] = new(Valuation, NotificationPriority.Normal, "valuation", false),
        [NotificationType.ValuationInquiryReminderSoon] = new(Valuation, NotificationPriority.Normal, "valuation", false),

        [NotificationType.PhoneVerification] = new(Account, NotificationPriority.High, "phone-verify", true),
        [NotificationType.UserRated] = new(Social, NotificationPriority.Low, "profile", false),
    };

    /// <summary>True when the type has an explicit row (the catalog test requires this for every type).</summary>
    public static bool IsDefined(NotificationType type) => Kinds.ContainsKey(type);

    public static NotificationKind Describe(NotificationType type) =>
        Kinds.TryGetValue(type, out var kind) ? kind : Fallback;

    public static NotificationTarget TargetFor(NotificationType type, Guid? propertyId, Guid? relatedEntityId)
    {
        var kind = Describe(type);
        return kind.TargetKind switch
        {
            // The related id of a message notification is the message id: the thread is the property's.
            "messages" => new NotificationTarget(kind.TargetKind, null, propertyId),
            "property" or "my-listings" => new NotificationTarget(kind.TargetKind, null, propertyId),
            "phone-verify" or "notifications" => new NotificationTarget(kind.TargetKind, null, null),
            "profile" => new NotificationTarget(kind.TargetKind, relatedEntityId, null),
            _ => new NotificationTarget(kind.TargetKind, relatedEntityId, propertyId),
        };
    }
}
