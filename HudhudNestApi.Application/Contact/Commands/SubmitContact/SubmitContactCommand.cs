using MediatR;

namespace HudhudNestApi.Application.Contact.Commands.SubmitContact;

public sealed record SubmitContactCommand(
    string Name,
    string Email,
    string? Subject,
    string Message,
    string? IpAddress) : IRequest<Guid>;

