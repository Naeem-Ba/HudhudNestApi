using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Contact.Interfaces;
using HudhudNestApi.Application.Contact.Services;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Contact.Commands.SubmitContact;

public sealed class SubmitContactCommandHandler
    : IRequestHandler<SubmitContactCommand, Guid>
{
    private readonly IContactMessageRepository _messages;
    private readonly IUnitOfWork _uow;
    private readonly ContactMessageNotifier _notifier;

    public SubmitContactCommandHandler(
        IContactMessageRepository messages, IUnitOfWork uow, ContactMessageNotifier notifier)
    {
        _messages = messages;
        _uow = uow;
        _notifier = notifier;
    }

    public async Task<Guid> Handle(
        SubmitContactCommand request,
        CancellationToken cancellationToken)
    {
        var message = new ContactMessage
        {
            Name = request.Name.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Subject = request.Subject?.Trim(),
            Body = request.Message.Trim(),
            IpAddress = request.IpAddress
        };

        _messages.Add(message);
        await _uow.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAdminsAsync(message, cancellationToken);

        return message.Id;
    }
}

