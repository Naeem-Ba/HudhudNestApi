using MediatR;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.ResolveDeadLetter;
using HudhudNestApi.Application.SocialDistribution.Commands.RetrySocialPublication;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class ResolveDeadLetterCommandHandlerTests
{
    private static SocialPublicationDeadLetter MakeDeadLetter() => SocialPublicationDeadLetter.Create(
        Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook, SocialPublicationErrorCode.RateLimited, "rate limited", 5, DateTime.UtcNow);

    [Fact]
    public async Task Handle_WithoutRequeue_OnlyResolves_NeverCallsRetry()
    {
        var deadLetters = new Mock<ISocialPublicationDeadLetterRepository>();
        var uow = new Mock<IUnitOfWork>();
        var sender = new Mock<ISender>();
        var deadLetter = MakeDeadLetter();

        deadLetters.Setup(x => x.GetByIdAsync(deadLetter.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deadLetter);

        var handler = new ResolveDeadLetterCommandHandler(deadLetters.Object, uow.Object, sender.Object);
        var result = await handler.Handle(new ResolveDeadLetterCommand(deadLetter.Id, Guid.NewGuid(), "تم الإصلاح.", Requeue: false), CancellationToken.None);

        Assert.NotNull(result.ResolvedAt);
        sender.Verify(x => x.Send(It.IsAny<RetrySocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithRequeue_ResolvesAndSendsRetryCommand()
    {
        var deadLetters = new Mock<ISocialPublicationDeadLetterRepository>();
        var uow = new Mock<IUnitOfWork>();
        var sender = new Mock<ISender>();
        var deadLetter = MakeDeadLetter();
        var actorId = Guid.NewGuid();

        deadLetters.Setup(x => x.GetByIdAsync(deadLetter.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deadLetter);
        sender.Setup(x => x.Send(It.IsAny<RetrySocialPublicationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SocialPublicationDto)null!);

        var handler = new ResolveDeadLetterCommandHandler(deadLetters.Object, uow.Object, sender.Object);
        await handler.Handle(new ResolveDeadLetterCommand(deadLetter.Id, actorId, null, Requeue: true), CancellationToken.None);

        sender.Verify(x => x.Send(
            It.Is<RetrySocialPublicationCommand>(c => c.PublicationId == deadLetter.PublicationId && c.ActorUserId == actorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsNotFound()
    {
        var deadLetters = new Mock<ISocialPublicationDeadLetterRepository>();
        deadLetters.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((SocialPublicationDeadLetter?)null);

        var handler = new ResolveDeadLetterCommandHandler(deadLetters.Object, Mock.Of<IUnitOfWork>(), Mock.Of<ISender>());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ResolveDeadLetterCommand(Guid.NewGuid(), Guid.NewGuid(), null, false), CancellationToken.None));
    }
}
