using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.Messaging.DTOs;
using HudhudNestApi.Application.Users.Messaging.Interfaces;

namespace HudhudNestApi.Application.Users.Messaging.Queries.GetConversations;

public sealed class GetConversationsQueryHandler
    : IRequestHandler<GetConversationsQuery, PagedResult<ConversationSummaryDto>>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IMessageRepository _messages;

    public GetConversationsQueryHandler(
        ICurrentUserService currentUser,
        IMessageRepository messages)
    {
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<PagedResult<ConversationSummaryDto>> Handle(
        GetConversationsQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException(
                "Authentication is required to read messages.");

        return await _messages.GetConversationSummariesAsync(
            currentUserId,
            request.PropertyId,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}
