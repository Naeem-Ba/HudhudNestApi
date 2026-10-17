namespace HudhudNestApi.Application.Users.Messaging;

/// <summary>
/// How much one user may write about one listing (QA Phase 8, P8-D07). The HTTP limiter allows 60 writes
/// a minute, which let one visitor send dozens of messages a minute to one owner, each raising a
/// notification. These limits are per (sender, listing) and count stored messages.
/// </summary>
public static class MessagingLimits
{
    public static readonly TimeSpan BurstWindow = TimeSpan.FromMinutes(10);
    public const int MaxMessagesPerBurstWindow = 10;

    public static readonly TimeSpan DailyWindow = TimeSpan.FromHours(24);
    public const int MaxMessagesPerDay = 100;

    /// <summary>
    /// While the receiver still has an unread message from this sender about this listing that is newer
    /// than this, a further message does not raise another notification: the first one already points at
    /// the conversation.
    /// </summary>
    public static readonly TimeSpan NotificationCoalesceWindow = TimeSpan.FromMinutes(10);
}
