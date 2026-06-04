using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Users.Messaging.DTOs;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Domain.Messaging.Entities;

namespace PropertyApi.Application.Users.Messaging.Queries.GetMessages;

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
            ?? throw new UnauthorizedAccessException("Authentication is required to read messages.");

        var property = await _properties.GetByIdAsync(request.PropertyId, cancellationToken);
        if (property is null)
            throw new KeyNotFoundException("Property was not found.");

        if (request.OtherUserId.HasValue)
        {
            var conversation = await _messages.GetConversationAsync(
                request.PropertyId,
                currentUserId,
                request.OtherUserId.Value,
                cancellationToken);

            return ToPagedResult(
                conversation,
                request.Page,
                request.PageSize);
        }

        if (property.OwnerId != currentUserId)
            throw new UnauthorizedAccessException(
                "Only the property owner can list all property messages. Provide OtherUserId to read a conversation.");

        var pagedMessages = await _messages.GetByPropertyAsync(
            request.PropertyId,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<MessageDto>
        {
            Items = pagedMessages.Items.Select(MapToDto).ToList().AsReadOnly(),
            TotalCount = pagedMessages.TotalCount,
            Page = pagedMessages.Page,
            PageSize = pagedMessages.PageSize
        };
    }

    private static PagedResult<MessageDto> ToPagedResult(
        IReadOnlyList<Message> messages,
        int page,
        int pageSize)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 50);

        return new PagedResult<MessageDto>
        {
            Items = messages
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .Select(MapToDto)
                .ToList()
                .AsReadOnly(),
            TotalCount = messages.Count,
            Page = safePage,
            PageSize = safePageSize
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
