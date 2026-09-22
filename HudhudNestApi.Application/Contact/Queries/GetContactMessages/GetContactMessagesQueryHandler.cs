using MediatR;
using HudhudNestApi.Application.Contact.DTOs;
using HudhudNestApi.Application.Contact.Interfaces;

namespace HudhudNestApi.Application.Contact.Queries.GetContactMessages;

public sealed class GetContactMessagesQueryHandler
    : IRequestHandler<GetContactMessagesQuery, ContactMessagesPageDto>
{
    private readonly IContactMessageRepository _messages;

    public GetContactMessagesQueryHandler(IContactMessageRepository messages)
        => _messages = messages;

    public Task<ContactMessagesPageDto> Handle(
        GetContactMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        return _messages.GetPageAsync(page, pageSize, cancellationToken);
    }
}

