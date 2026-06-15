using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.DTOs;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Users.Messaging.Commands.SendMessage;

public sealed class SendMessageCommandHandler
    : IRequestHandler<SendMessageCommand, MessageDto>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPropertyRepository _properties;
    private readonly IUserRepository _users;
    private readonly IMessageRepository _messages;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    public SendMessageCommandHandler(
        ICurrentUserService currentUser,
        IPropertyRepository properties,
        IUserRepository users,
        IMessageRepository messages,
        IUnitOfWork uow,
        INotificationService notifications,
        ILogger<SendMessageCommandHandler> logger)
    {
        _currentUser = currentUser;
        _properties = properties;
        _users = users;
        _messages = messages;
        _uow = uow;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<MessageDto> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        var senderId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("Authentication is required to send messages.");

        var property = await _properties.GetByIdWithDetailsAsync(
            request.PropertyId,
            cancellationToken);

        if (property is null)
            throw new KeyNotFoundException("Property was not found.");

        Guid receiverId;

        if (senderId != property.OwnerId)
        {
            // A visitor can only contact the property owner.
            receiverId = property.OwnerId;
        }
        else
        {
            // The owner must explicitly select the user being answered.
            if (!request.ReceiverId.HasValue ||
                request.ReceiverId.Value == Guid.Empty)
            {
                throw new ValidationException(
                [
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.ReceiverId),
                        "ReceiverId is required when the property owner replies.")
                ]);
            }

            receiverId = request.ReceiverId.Value;

            var conversationExists =
                await _messages.ConversationExistsAsync(
                    property.Id,
                    senderId,
                    receiverId,
                    cancellationToken);

            if (!conversationExists)
            {
                throw new ForbiddenException(
                    "The property owner can only reply to an existing conversation.");
            }
        }

        if (receiverId == senderId)
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.ReceiverId),
                    "You cannot send a message to yourself.")
            ]);
        }

        if (!await _users.ExistsAsync(receiverId, cancellationToken))
        {
            throw new NotFoundException("Receiver was not found.");
        }

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
        var senderDisplayName = BuildSenderDisplayName(sender);

        // Notification is non-critical: message persistence has already succeeded.
        try
        {
            await _notifications.NotifyNewMessageAsync(
                recipientId: receiverId,
                senderId: senderId,
                senderName: senderDisplayName,
                messageId: message.Id,
                propertyId: property.Id,
                ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send message notification. MessageId={MessageId}, PropertyId={PropertyId}, SenderId={SenderId}, ReceiverId={ReceiverId}",
                message.Id,
                property.Id,
                senderId,
                receiverId);
        }

        return new MessageDto
        {
            Id = message.Id,
            PropertyId = message.PropertyId,
            SenderId = message.SenderId,
            ReceiverId = message.ReceiverId,
            SenderDisplayName = senderDisplayName,
            Content = message.Content,
            IsRead = message.IsRead,
            ReadAt = message.ReadAt,
            CreatedAt = message.CreatedAt
        };
    }

    private static string BuildSenderDisplayName(User? sender)
    {
        if (sender is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(sender.DisplayName))
            return sender.DisplayName;

        var fullName = $"{sender.FirstName} {sender.LastName}".Trim();

        return string.IsNullOrWhiteSpace(fullName)
            ? "Ù…Ø³ØªØ®Ø¯Ù…"
            : fullName;
    }
}

