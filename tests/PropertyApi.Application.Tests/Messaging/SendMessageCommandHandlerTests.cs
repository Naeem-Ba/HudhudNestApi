using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.Commands.SendMessage;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Tests.Messaging;

public sealed class SendMessageCommandHandlerTests
{
    [Fact(DisplayName = "A message for an unknown property is a NotFound (HTTP 404), not an unhandled 500")]
    public async Task Handle_UnknownProperty_ThrowsNotFoundException()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        var properties = new Mock<IPropertyRepository>();
        properties
            .Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Property?)null);

        var handler = new SendMessageCommandHandler(
            currentUser.Object,
            properties.Object,
            Mock.Of<IUserDirectoryReadService>(),
            Mock.Of<IMessageRepository>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<INotificationService>(),
            NullLogger<SendMessageCommandHandler>.Instance);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SendMessageCommand(Guid.NewGuid(), null, "hello"),
            CancellationToken.None));

        Assert.Equal("Property was not found.", exception.Message);
    }
}
