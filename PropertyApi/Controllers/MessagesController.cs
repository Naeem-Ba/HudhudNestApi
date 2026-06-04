using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MessagesController : ControllerBase
{
    // -- GET /api/messages?propertyId={guid} ------------------
    // Returns all messages for a property (for the property owner)
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public Task<IActionResult> GetMessages(
        [FromQuery] Guid? propertyId,
        CancellationToken ct)
    {
        // TODO Phase 2: return await _mediator.Send(new GetMessagesQuery(propertyId), ct);
        return Task.FromResult<IActionResult>(
            StatusCode(StatusCodes.Status501NotImplemented,
                new { message = "Messaging feature coming in Phase 2." }));
    }

    // -- POST /api/messages ------------------------------------
    // Send a message about a property
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public Task<IActionResult> SendMessage(
        [FromBody] object body,
        CancellationToken ct)
    {
        // TODO Phase 2: return await _mediator.Send(new SendMessageCommand(...), ct);
        return Task.FromResult<IActionResult>(
            StatusCode(StatusCodes.Status501NotImplemented,
                new { message = "Messaging feature coming in Phase 2." }));
    }
}
