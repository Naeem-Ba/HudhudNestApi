using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Contact.Interfaces;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Contact.Commands.SubmitContact;

public sealed class SubmitContactCommandHandler
    : IRequestHandler<SubmitContactCommand, Guid>
{
    private readonly IContactMessageRepository _messages;
    private readonly IUnitOfWork _uow;

    public SubmitContactCommandHandler(IContactMessageRepository messages, IUnitOfWork uow)
    {
        _messages = messages;
        _uow = uow;
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

        return message.Id;
    }
}

