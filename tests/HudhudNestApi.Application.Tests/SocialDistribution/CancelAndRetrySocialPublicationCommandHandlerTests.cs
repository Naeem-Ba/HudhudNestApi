using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.CancelSocialPublication;
using HudhudNestApi.Application.SocialDistribution.Commands.RetrySocialPublication;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class CancelAndRetrySocialPublicationCommandHandlerTests
{
    private static SocialPublication MakeDraftPublication()
    {
        var publication = SocialPublication.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook);
        var content = SocialPostContent.Create(
            publication.Id, SocialPlatform.Facebook, "عنوان", "نص", "https://cdn.example.com/img.jpg",
            "https://hudhudnest.com/properties/p1", null, "ar");
        publication.AttachContent(content);
        return publication;
    }

    [Fact]
    public async Task Cancel_HappyPath_MovesToCancelled()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var history = new Mock<ISocialPublicationStatusHistoryRepository>();
        var uow = new Mock<IUnitOfWork>();
        var publication = MakeDraftPublication();

        publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);

        var handler = new CancelSocialPublicationCommandHandler(publications.Object, history.Object, uow.Object);
        var result = await handler.Handle(new CancelSocialPublicationCommand(publication.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Cancelled, result.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_NotFound_ThrowsNotFound()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var history = new Mock<ISocialPublicationStatusHistoryRepository>();
        var uow = new Mock<IUnitOfWork>();
        var id = Guid.NewGuid();

        publications.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((SocialPublication?)null);

        var handler = new CancelSocialPublicationCommandHandler(publications.Object, history.Object, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CancelSocialPublicationCommand(id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_AlreadyPublished_ThrowsInvalidStateTransition()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var history = new Mock<ISocialPublicationStatusHistoryRepository>();
        var uow = new Mock<IUnitOfWork>();
        var publication = MakeDraftPublication();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkPublished("ext-1", null, DateTime.UtcNow);

        publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);

        var handler = new CancelSocialPublicationCommandHandler(publications.Object, history.Object, uow.Object);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            handler.Handle(new CancelSocialPublicationCommand(publication.Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Retry_HappyPath_FromFailed_MovesToQueued()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var history = new Mock<ISocialPublicationStatusHistoryRepository>();
        var uow = new Mock<IUnitOfWork>();
        var publication = MakeDraftPublication();
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkFailed(SocialPublicationErrorCode.InvalidCredentials, "bad token", DateTime.UtcNow, TimeSpan.Zero);

        publications.Setup(x => x.GetByIdAsync(publication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(publication);

        var handler = new RetrySocialPublicationCommandHandler(publications.Object, history.Object, uow.Object);
        var result = await handler.Handle(new RetrySocialPublicationCommand(publication.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(SocialPublicationStatus.Queued, result.Status);
    }

    [Fact]
    public async Task Retry_NotFound_ThrowsNotFound()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var history = new Mock<ISocialPublicationStatusHistoryRepository>();
        var uow = new Mock<IUnitOfWork>();
        var id = Guid.NewGuid();

        publications.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((SocialPublication?)null);

        var handler = new RetrySocialPublicationCommandHandler(publications.Object, history.Object, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RetrySocialPublicationCommand(id, Guid.NewGuid()), CancellationToken.None));
    }
}
