using MediatR;

namespace HudhudNestApi.Application.Users.Messaging.Commands.MarkConversationRead;

/// <summary>
/// Marks every unread message the current user received from OtherUserId about
/// PropertyId as read. Returns how many messages changed. Only messages whose
/// receiver is the authenticated user are touched, so it cannot affect anyone
/// else's read state.
/// </summary>
public sealed record MarkConversationReadCommand(
    Guid PropertyId,
    Guid OtherUserId
) : IRequest<int>;
