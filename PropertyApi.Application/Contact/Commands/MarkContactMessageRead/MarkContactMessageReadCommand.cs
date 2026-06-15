using MediatR;

namespace PropertyApi.Application.Contact.Commands.MarkContactMessageRead;

public sealed record MarkContactMessageReadCommand(Guid Id) : IRequest<bool>;

