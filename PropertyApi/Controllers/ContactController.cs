using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Contact.Commands.DeleteContactMessage;
using PropertyApi.Application.Contact.Commands.MarkContactMessageRead;
using PropertyApi.Application.Contact.Commands.SubmitContact;
using PropertyApi.Application.Contact.DTOs;
using PropertyApi.Application.Contact.Queries.GetContactMessageById;
using PropertyApi.Application.Contact.Queries.GetContactMessages;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ContactController : ControllerBase
{
    private readonly ISender _sender;

    public ContactController(ISender sender)
        => _sender = sender;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("contact")]
    public async Task<IActionResult> Submit(
        [FromBody] ContactSubmitDto dto,
        CancellationToken ct)
    {
        if (dto is null)
            return BadRequest(new { message = "Request body is required." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (string.IsNullOrWhiteSpace(dto.Name) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Message))
        {
            return BadRequest(new { message = "Name, Email and Message are required." });
        }

        var id = await _sender.Send(
            new SubmitContactCommand(
                dto.Name,
                dto.Email,
                dto.Subject,
                dto.Message,
                HttpContext.Connection.RemoteIpAddress?.ToString()),
            ct);

        return Ok(new { success = true, id });
    }

    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetContactMessagesQuery(page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var message = await _sender.Send(new GetContactMessageByIdQuery(id), ct);
        return message is null ? NotFound() : Ok(message);
    }

    [HttpPut("{id:guid}/read")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        var updated = await _sender.Send(new MarkContactMessageReadCommand(id), ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _sender.Send(new DeleteContactMessageCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }
}
