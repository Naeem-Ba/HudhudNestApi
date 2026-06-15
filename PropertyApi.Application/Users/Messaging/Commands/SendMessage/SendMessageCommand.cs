using MediatR;
using PropertyApi.Application.Users.Messaging.DTOs;

namespace PropertyApi.Application.Users.Messaging.Commands.SendMessage;

/// <summary>
/// Sends a property-scoped message.
/// If ReceiverId is null, the message is sent to the property owner.
/// </summary>
public sealed record SendMessageCommand(
    Guid PropertyId,
    Guid? ReceiverId,
    string Content
) : IRequest<MessageDto>;

