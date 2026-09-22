using MediatR;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialPublication;
using HudhudNestApi.Application.SocialDistribution.Commands.RepostSocialPublication;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>Phase 13 spec §"Repost" — re-sharing delegates entirely to CreateSocialPublicationCommand with IsPromotionalRepost:true, never duplicating its validation.</summary>
public sealed class RepostSocialPublicationCommandHandlerTests
{
    private static SocialPublication MakePublishedPublication()
    {
        var publication = SocialPublication.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook);
        var content = SocialPostContent.Create(
            publication.Id, SocialPlatform.Facebook, "عنوان", "نص", "https://cdn.example.com/img.jpg",
            "https://realestateworld.world/properties/p1", new[] { "عقارات" }, "ar");
        publication.AttachContent(content);
        publication.Queue(null, DateTime.UtcNow);
        publication.StartPublishing(DateTime.UtcNow);
        publication.MarkPublished("ext-1", null, DateTime.UtcNow);
        return publication;
    }

    [Fact]
    public async Task Handle_PublishedOriginal_SendsCreateCommand_WithPromotionalRepostFlag()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var sender = new Mock<ISender>();
        var original = MakePublishedPublication();

        publications.Setup(x => x.GetByIdAsync(original.Id, It.IsAny<CancellationToken>())).ReturnsAsync(original);
        sender.Setup(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SocialPublicationDto)null!);

        var handler = new RepostSocialPublicationCommandHandler(publications.Object, sender.Object);
        await handler.Handle(new RepostSocialPublicationCommand(original.Id, Guid.NewGuid()), CancellationToken.None);

        sender.Verify(x => x.Send(
            It.Is<CreateSocialPublicationCommand>(c =>
                c.PropertyId == original.PropertyId &&
                c.SocialAccountId == original.SocialAccountId &&
                c.IsPromotionalRepost &&
                c.DistributionRuleId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OriginalNotPublished_ThrowsConflict_NeverSendsCreateCommand()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var sender = new Mock<ISender>();

        var draft = SocialPublication.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook);
        var content = SocialPostContent.Create(
            draft.Id, SocialPlatform.Facebook, "عنوان", "نص", "https://cdn.example.com/img.jpg",
            "https://realestateworld.world/properties/p1", null, "ar");
        draft.AttachContent(content);

        publications.Setup(x => x.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var handler = new RepostSocialPublicationCommandHandler(publications.Object, sender.Object);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new RepostSocialPublicationCommand(draft.Id, Guid.NewGuid()), CancellationToken.None));

        sender.Verify(x => x.Send(It.IsAny<CreateSocialPublicationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OriginalNotFound_ThrowsNotFound()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        publications.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((SocialPublication?)null);

        var handler = new RepostSocialPublicationCommandHandler(publications.Object, Mock.Of<ISender>());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RepostSocialPublicationCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }
}
