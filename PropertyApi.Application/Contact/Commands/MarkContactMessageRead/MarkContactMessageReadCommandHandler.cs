using MediatR;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Contact.Interfaces;

namespace PropertyApi.Application.Contact.Commands.MarkContactMessageRead;

public sealed class MarkContactMessageReadCommandHandler
    : IRequestHandler<MarkContactMessageReadCommand, bool>
{
    private readonly IContactMessageRepository _messages;
    private readonly IUnitOfWork _uow;

    public MarkContactMessageReadCommandHandler(IContactMessageRepository messages, IUnitOfWork uow)
    {
        _messages = messages;
        _uow = uow;
    }

    public async Task<bool> Handle(
        MarkContactMessageReadCommand request,
        CancellationToken cancellationToken)
    {
        var message = await _messages.GetByIdAsync(request.Id, cancellationToken);
        if (message is null)
            return false;

        message.IsRead = true;
        message.ReadAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(cancellationToken);

        return true;
    }
}
