using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Contact.Commands.MarkContactMessageRead;
using PropertyApi.Application.Contact.Commands.SubmitContact;
using PropertyApi.Application.Contact.Interfaces;
using PropertyApi.Domain.Messaging.Entities;

namespace PropertyApi.Auth.Tests.Application.Handlers;

public sealed class ContactHandlerTests
{
    [Fact(DisplayName = "Submit contact handler normalizes email and persists message")]
    public async Task SubmitContact_NormalizesEmail_And_Commits()
    {
        var repository = new Mock<IContactMessageRepository>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new SubmitContactCommandHandler(repository.Object, uow.Object);

        await handler.Handle(
            new SubmitContactCommand(" Naeem ", " TEST@EXAMPLE.COM ", " Subject ", " Message body ", "127.0.0.1"),
            CancellationToken.None);

        repository.Verify(x => x.Add(It.Is<ContactMessage>(message =>
            message.Name == "Naeem" &&
            message.Email == "test@example.com" &&
            message.Body == "Message body")), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Mark read handler returns false when contact message does not exist")]
    public async Task MarkRead_Missing_ReturnsFalse()
    {
        var repository = new Mock<IContactMessageRepository>();
        repository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContactMessage?)null);

        var handler = new MarkContactMessageReadCommandHandler(repository.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new MarkContactMessageReadCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result);
    }
}
