using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.Messaging.DTOs;
using HudhudNestApi.Application.Users.Messaging.Interfaces;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Users.Messaging.Queries.GetMessages;

public sealed class GetMessagesQueryHandler
    : IRequestHandler<GetMessagesQuery, PagedResult<MessageDto>>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPropertyRepository _properties;
    private readonly IMessageRepository _messages;

    public GetMessagesQueryHandler(
        ICurrentUserService currentUser,
        IPropertyRepository properties,
        IMessageRepository messages)
    {
        _currentUser = currentUser;
        _properties = properties;
        _messages = messages;
    }

    public async Task<PagedResult<MessageDto>> Handle(
        GetMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException(
                "Authentication is required to read messages.");

        var property = await _properties.GetByIdAsync(
            request.PropertyId,
            cancellationToken);

        if (property is null)
            throw new NotFoundException("Property was not found.");

        if (request.OtherUserId.HasValue)
        {
            var pagedConversation = await _messages.GetConversationAsync(
                request.PropertyId,
                currentUserId,
                request.OtherUserId.Value,
                request.Page,
                request.PageSize,
                cancellationToken);

            return new PagedResult<MessageDto>
            {
                Items = pagedConversation.Items
                    .Select(MapToDto)
                    .ToList()
                    .AsReadOnly(),
                TotalCount = pagedConversation.TotalCount,
                Page = pagedConversation.Page,
                PageSize = pagedConversation.PageSize
            };
        }

        if (property.OwnerId != currentUserId)
        {
            throw new UnauthorizedAccessException(
                "Only the property owner can list all property messages. Provide OtherUserId to read a conversation.");
        }

        var pagedMessages = await _messages.GetByPropertyAsync(
            request.PropertyId,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<MessageDto>
        {
            Items = pagedMessages.Items
                .Select(MapToDto)
                .ToList()
                .AsReadOnly(),
            TotalCount = pagedMessages.TotalCount,
            Page = pagedMessages.Page,
            PageSize = pagedMessages.PageSize
        };
    }

    private static MessageDto MapToDto(Message message) => new()
    {
        Id = message.Id,
        PropertyId = message.PropertyId,
        SenderId = message.SenderId,
        ReceiverId = message.ReceiverId,
        SenderDisplayName = message.Sender is null
            ? string.Empty
            : (!string.IsNullOrWhiteSpace(message.Sender.DisplayName)
                ? message.Sender.DisplayName
                : $"{message.Sender.FirstName} {message.Sender.LastName}".Trim()),
        Content = message.Content,
        IsRead = message.IsRead,
        ReadAt = message.ReadAt,
        CreatedAt = message.CreatedAt
    };
}
