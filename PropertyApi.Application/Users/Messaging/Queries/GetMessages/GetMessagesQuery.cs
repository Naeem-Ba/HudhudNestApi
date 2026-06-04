using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Users.Messaging.DTOs;

namespace PropertyApi.Application.Users.Messaging.Queries.GetMessages;

/// <summary>
/// Gets messages related to a property.
/// Property owner can list all property messages.
/// Other users must request a conversation using OtherUserId.
/// </summary>
public sealed record GetMessagesQuery(
    Guid PropertyId,
    Guid? OtherUserId,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<MessageDto>>;
