using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Users.Messaging.DTOs;
using PropertyApi.Application.Users.Messaging.Interfaces;

namespace PropertyApi.Application.Users.Messaging.Queries.GetConversations;

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
