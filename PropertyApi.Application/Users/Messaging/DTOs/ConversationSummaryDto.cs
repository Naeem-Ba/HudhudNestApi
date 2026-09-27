namespace PropertyApi.Application.Users.Messaging.DTOs;

/// <summary>
/// One row of the current user's inbox: a conversation is every message
/// exchanged between the current user and one other user about one property
/// (the same PropertyId + participant pair GetMessagesQuery already reads).
/// </summary>
public sealed class ConversationSummaryDto
{
    public Guid PropertyId { get; set; }
    public string PropertyTitle { get; set; } = string.Empty;
    public string? PropertyImageUrl { get; set; }

    /// <summary>True when the current user owns the property (i.e. this is an inquiry they received).</summary>
    public bool IsPropertyOwner { get; set; }

    public Guid OtherUserId { get; set; }
    public string OtherUserDisplayName { get; set; } = string.Empty;
    public string? OtherUserImageUrl { get; set; }

    public Guid LastMessageSenderId { get; set; }
    public string LastMessageContent { get; set; } = string.Empty;
    public DateTime LastMessageAt { get; set; }

    /// <summary>Messages in this conversation sent to the current user and not yet read.</summary>
    public int UnreadCount { get; set; }
}
