using MediatR;
using HudhudNestApi.Application.Users.Messaging.DTOs;

namespace HudhudNestApi.Application.Users.Messaging.Commands.SendMessage;

/// <summary>
/// Sends a property-scoped message.
/// If ReceiverId is null, the message is sent to the property owner.
/// </summary>
public sealed record SendMessageCommand(
    Guid PropertyId,
    Guid? ReceiverId,
    string Content
) : IRequest<MessageDto>;

