using MediatR;

namespace PropertyApi.Application.Contact.Commands.SubmitContact;

public sealed record SubmitContactCommand(
    string Name,
    string Email,
    string? Subject,
    string Message,
    string? IpAddress) : IRequest<Guid>;
