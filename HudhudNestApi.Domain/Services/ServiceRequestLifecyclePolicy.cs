namespace HudhudNestApi.Domain.Services;

/// <summary>
/// The clock of a service request before it is delivered: when the customer is reminded to confirm,
/// and when a request nobody touches is closed by the system. One place, so the sweep, the admin
/// "needs attention" rule and the tests cannot drift apart.
/// </summary>
public static class ServiceRequestLifecyclePolicy
{
    /// <summary>An inquiry the customer has not confirmed gets one in-app reminder after this long.</summary>
    public static readonly TimeSpan ConfirmReminderAfter = TimeSpan.FromHours(48);

    /// <summary>No message and no status change for this long closes the request as Expired.</summary>
    public static readonly TimeSpan ExpireAfterInactivity = TimeSpan.FromDays(30);

    /// <summary>Messages per request. A thread is a negotiation, not a chat room.</summary>
    public const int MaxMessagesPerRequest = 300;

    public const int MaxMessageLength = 1000;
}
