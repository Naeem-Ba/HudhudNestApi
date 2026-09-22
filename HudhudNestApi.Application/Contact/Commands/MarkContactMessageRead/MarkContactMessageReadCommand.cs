using MediatR;

namespace HudhudNestApi.Application.Contact.Commands.MarkContactMessageRead;

public sealed record MarkContactMessageReadCommand(Guid Id) : IRequest<bool>;

