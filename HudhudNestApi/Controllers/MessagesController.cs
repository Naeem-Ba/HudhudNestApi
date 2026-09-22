using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Users.Messaging.Commands.SendMessage;
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

    [HttpPost]
    public async Task<IActionResult> SendMessage(
        [FromBody] SendMessageCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(201, result);
    }
}