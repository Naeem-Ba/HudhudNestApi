using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Contact.Interfaces;

namespace PropertyApi.Application.Contact.Commands.DeleteContactMessage;

public sealed class DeleteContactMessageCommandHandler
    : IRequestHandler<DeleteContactMessageCommand, bool>
{
    private readonly IContactMessageRepository _messages;
    private readonly IUnitOfWork _uow;

    public DeleteContactMessageCommandHandler(IContactMessageRepository messages, IUnitOfWork uow)
    {
        _messages = messages;
        _uow = uow;
    }

    public async Task<bool> Handle(
        DeleteContactMessageCommand request,
        CancellationToken cancellationToken)
    {
        var message = await _messages.GetByIdAsync(request.Id, cancellationToken);
        if (message is null)
            return false;

        _messages.Remove(message);
        await _uow.SaveChangesAsync(cancellationToken);

        return true;
    }
}
