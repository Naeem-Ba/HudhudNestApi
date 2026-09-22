using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.Messaging.DTOs;

namespace HudhudNestApi.Application.Users.Messaging.Queries.GetMessages;

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

