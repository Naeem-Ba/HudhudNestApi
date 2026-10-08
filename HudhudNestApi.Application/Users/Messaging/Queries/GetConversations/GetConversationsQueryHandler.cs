using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.Messaging.DTOs;
using HudhudNestApi.Application.Users.Messaging.Interfaces;

namespace HudhudNestApi.Application.Users.Messaging.Queries.GetConversations;

public sealed class GetConversationsQueryHandler
    : IRequestHandler<GetConversationsQuery, PagedResult<ConversationSummaryDto>>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IMessageRepository _messages;
    private readonly IAgencyRepository _agencies;
    private readonly IPropertyRepository _properties;

    public GetConversationsQueryHandler(
        ICurrentUserService currentUser,
        IMessageRepository messages,
        IAgencyRepository agencies,
        IPropertyRepository properties)
    {
        _currentUser = currentUser;
        _messages = messages;
        _agencies = agencies;
        _properties = properties;
    }

    public async Task<PagedResult<ConversationSummaryDto>> Handle(
        GetConversationsQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException(
                "Authentication is required to read messages.");

        // Read-only oversight: for one listing of their agency, the agency owner sees the
        // publisher's conversations about it (their own inbox for other listings is unchanged).
        var inboxUserId = currentUserId;
        if (request.PropertyId is { } propertyId)
        {
            var property = await _properties.GetByIdAsync(propertyId, cancellationToken);
            if (property is not null &&
                property.OwnerId != currentUserId &&
                await AgencyOwnerAccess.IsOwnerOfListingAgencyAsync(_agencies, property, currentUserId, cancellationToken))
            {
                inboxUserId = property.OwnerId;
            }
        }

        return await _messages.GetConversationSummariesAsync(
            inboxUserId,
            request.PropertyId,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}
