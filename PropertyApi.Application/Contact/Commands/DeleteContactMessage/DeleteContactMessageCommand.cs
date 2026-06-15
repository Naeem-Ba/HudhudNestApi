using MediatR;

namespace PropertyApi.Application.Contact.Commands.DeleteContactMessage;

public sealed record DeleteContactMessageCommand(Guid Id) : IRequest<bool>;

