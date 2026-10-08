namespace HudhudNestApi.Application.Notifications.Models;

/// <summary>
/// The names behind a message-based notification, so its text can be written in the reader's
/// language at read time. Any value may be null (deleted account, deleted listing, listing
/// without an agency); the text templates carry a localized fallback for each.
/// </summary>
public sealed record NotificationMessageContext(
    string? SenderName,
    string? ReceiverName,
    string? PropertyTitle,
    string? AgencyName);
