using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Contact.Interfaces;

namespace HudhudNestApi.Application.Contact.Commands.MarkContactMessageRead;

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

