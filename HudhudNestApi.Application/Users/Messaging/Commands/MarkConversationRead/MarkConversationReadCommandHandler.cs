using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Messaging.Interfaces;

namespace HudhudNestApi.Application.Users.Messaging.Commands.MarkConversationRead;

public sealed class MarkConversationReadCommandHandler
    : IRequestHandler<MarkConversationReadCommand, int>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IMessageRepository _messages;
    private readonly IUnitOfWork _uow;

    public MarkConversationReadCommandHandler(
        ICurrentUserService currentUser,
        IMessageRepository messages,
        IUnitOfWork uow)
    {
        _currentUser = currentUser;
        _messages = messages;
        _uow = uow;
    }

    public async Task<int> Handle(
        MarkConversationReadCommand request,
        CancellationToken cancellationToken)
    {
        var currentUserId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException(
                "Authentication is required to read messages.");

        var unread = await _messages.GetUnreadInConversationAsync(
            request.PropertyId,
            receiverId: currentUserId,
            senderId: request.OtherUserId,
            cancellationToken);

        if (unread.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;

        foreach (var message in unread)
        {
            message.IsRead = true;
            message.ReadAt = now;
            message.UpdatedAt = now;
        }

        await _uow.SaveChangesAsync(cancellationToken);

        return unread.Count;
    }
}
