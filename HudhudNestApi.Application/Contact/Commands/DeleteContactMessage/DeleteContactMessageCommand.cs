using MediatR;

namespace HudhudNestApi.Application.Contact.Commands.DeleteContactMessage;

public sealed record DeleteContactMessageCommand(Guid Id) : IRequest<bool>;

