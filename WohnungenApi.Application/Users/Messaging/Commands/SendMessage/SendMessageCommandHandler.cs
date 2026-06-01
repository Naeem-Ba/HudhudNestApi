using MediatR;
using WohnungenApi.Application.Common.Interfaces;
using WohnungenApi.Application.Listings.Interfaces;
using WohnungenApi.Application.Users.Interfaces;
using WohnungenApi.Application.Users.Messaging.DTOs;
using WohnungenApi.Application.Users.Messaging.Interfaces;
using WohnungenApi.Domain.Messaging.Entities;

namespace WohnungenApi.Application.Users.Messaging.Commands.SendMessage;

public sealed class SendMessageCommandHandler
    : IRequestHandler<SendMessageCommand, MessageDto>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPropertyRepository _properties;
    private readonly IUserRepository _users;
    private readonly IMessageRepository _messages;
    private readonly IUnitOfWork _uow;

    public SendMessageCommandHandler(
        ICurrentUserService currentUser,
        IPropertyRepository properties,
        IUserRepository users,
        IMessageRepository messages,
        IUnitOfWork uow)
    {
        _currentUser = currentUser;
        _properties = properties;
        _users = users;
        _messages = messages;
        _uow = uow;
    }

    public async Task<MessageDto> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        var senderId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("Authentication is required to send messages.");

        var property = await _properties.GetByIdWithDetailsAsync(request.PropertyId, cancellationToken);
        if (property is null)
            throw new KeyNotFoundException("Property was not found.");

        var receiverId = request.ReceiverId ?? property.OwnerId;
        if (receiverId == Guid.Empty)
            throw new InvalidOperationException("ReceiverId is required.");

        if (receiverId == senderId)
            throw new InvalidOperationException("You cannot send a message to yourself.");

        if (!await _users.ExistsAsync(receiverId, cancellationToken))
            throw new KeyNotFoundException("Receiver was not found.");

        var message = new Message
        {
            Content = request.Content.Trim(),
            PropertyId = property.Id,
            SenderId = senderId,
            ReceiverId = receiverId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _messages.AddAsync(message, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var sender = await _users.GetByIdAsync(senderId, cancellationToken);

        return new MessageDto
        {
            Id = message.Id,
            PropertyId = message.PropertyId,
            SenderId = message.SenderId,
            ReceiverId = message.ReceiverId,
            SenderDisplayName = sender is null
                ? string.Empty
                : (!string.IsNullOrWhiteSpace(sender.DisplayName)
                    ? sender.DisplayName
                    : $"{sender.FirstName} {sender.LastName}".Trim()),
            Content = message.Content,
            IsRead = message.IsRead,
            ReadAt = message.ReadAt,
            CreatedAt = message.CreatedAt
        };
    }
}
