using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Users.Messaging.DTOs;

namespace PropertyApi.Application.Users.Messaging.Queries.GetConversations;

/// <summary>
/// The current user's inbox across all properties, newest conversation first.
/// The user is always taken from the authenticated principal — never from the
/// request — so one user can never list another user's conversations.
/// PropertyId optionally narrows it to a single property (the owner's
/// per-listing view).
/// </summary>
public sealed record GetConversationsQuery(
    Guid? PropertyId = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<ConversationSummaryDto>>;
