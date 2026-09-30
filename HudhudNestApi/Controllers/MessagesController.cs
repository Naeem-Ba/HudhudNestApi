using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Users.Messaging.Commands.MarkConversationRead;
using HudhudNestApi.Application.Users.Messaging.Commands.SendMessage;
using HudhudNestApi.Application.Users.Messaging.Queries.GetConversations;
using HudhudNestApi.Application.Users.Messaging.Queries.GetMessages;

namespace HudhudNestApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MessagesController : ControllerBase
{
    private readonly ISender _mediator;
    public MessagesController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetMessages(
        [FromQuery] GetMessagesQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    // GET /api/Messages/conversations?propertyId=&page=&pageSize=
    // The current user's inbox across all properties (or one property when
    // propertyId is given). Scoped to the authenticated user by the handler.
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations(
        [FromQuery] Guid? propertyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetConversationsQuery(propertyId, page, pageSize), ct);
        return Ok(result);
    }

    // POST /api/Messages/conversations/read { propertyId, otherUserId }
    // Marks the messages the current user received in that conversation as read.
    [HttpPost("conversations/read")]
    public async Task<IActionResult> MarkConversationRead(
        [FromBody] MarkConversationReadCommand command, CancellationToken ct)
    {
        var updated = await _mediator.Send(command, ct);
        return Ok(new { updated });
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(
        [FromBody] SendMessageCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(201, result);
    }
}