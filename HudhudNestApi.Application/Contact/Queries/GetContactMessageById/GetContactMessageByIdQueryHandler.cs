using MediatR;
using HudhudNestApi.Application.Contact.DTOs;
using HudhudNestApi.Application.Contact.Interfaces;

namespace HudhudNestApi.Application.Contact.Queries.GetContactMessageById;

public sealed class GetContactMessageByIdQueryHandler
    : IRequestHandler<GetContactMessageByIdQuery, ContactMessageDto?>
{
    private readonly IContactMessageRepository _messages;

    public GetContactMessageByIdQueryHandler(IContactMessageRepository messages)
        => _messages = messages;

    public async Task<ContactMessageDto?> Handle(
        GetContactMessageByIdQuery request,
        CancellationToken cancellationToken)
    {
        var message = await _messages.GetByIdAsync(request.Id, cancellationToken);
        if (message is null)
            return null;

        return new ContactMessageDto(
            message.Id,
            message.Name,
            message.Email,
            message.Subject,
            message.Body,
            message.IsRead,
            message.CreatedAt);
    }
}

