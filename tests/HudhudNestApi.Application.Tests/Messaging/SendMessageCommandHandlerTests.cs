using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Application.Users.Messaging;
using HudhudNestApi.Application.Users.Messaging.Commands.SendMessage;
using HudhudNestApi.Application.Users.Messaging.Interfaces;
using HudhudNestApi.Application.Users.Models;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Messaging.Entities;

namespace HudhudNestApi.Application.Tests.Messaging;

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
            Mock.Of<IAgencyRepository>(),
            NullLogger<SendMessageCommandHandler>.Instance);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SendMessageCommand(Guid.NewGuid(), null, "hello"),
            CancellationToken.None));

        Assert.Equal("Property was not found.", exception.Message);
    }

    // ---- agency member workflow --------------------------------------------------------------

    private sealed class Fixture
    {
        public Guid Customer { get; } = Guid.NewGuid();
        public Guid Member { get; } = Guid.NewGuid();
        public Guid AgencyOwner { get; } = Guid.NewGuid();
        public Property Property { get; }
        public Agency Agency { get; }
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IMessageRepository> Messages { get; } = new();
        public Mock<IAgencyRepository> Agencies { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();
        public Message? Saved { get; private set; }

        public Fixture(bool agencyListing = true, bool ownerPublishes = false)
        {
            var publisher = ownerPublishes ? AgencyOwner : Member;
            Agency = Agency.Create("XYZ Real Estate", "xyz", AgencyOwner, "SY", DateTime.UtcNow);
            Property = Property.Create("Sea view flat", "A description", publisher, ListingType.ForRent);
            if (agencyListing)
                Property.SetAgency(Agency.Id);

            Agencies.Setup(x => x.GetByIdAsync(Agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Agency);
            Messages
                .Setup(x => x.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Callback<Message, CancellationToken>((m, _) => Saved = m)
                .Returns(Task.CompletedTask);
            Messages
                .Setup(x => x.ConversationExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        public SendMessageCommandHandler Handler(Guid asUser)
        {
            CurrentUser.SetupGet(x => x.UserId).Returns(asUser);
            var properties = new Mock<IPropertyRepository>();
            properties
                .Setup(x => x.GetByIdWithDetailsAsync(Property.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Property);
            var directory = new Mock<IUserDirectoryReadService>();
            directory
                .Setup(x => x.GetActiveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new UserDirectoryEntry(id, $"user-{id.ToString()[..4]}", null));
            var uow = new Mock<IUnitOfWork>();
            uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            return new SendMessageCommandHandler(
                CurrentUser.Object, properties.Object, directory.Object, Messages.Object,
                uow.Object, Notifications.Object, Agencies.Object, NullLogger<SendMessageCommandHandler>.Instance);
        }
    }

    [Fact(DisplayName = "Customer -> agency member: message goes to the publisher AND the agency owner is notified (no message text)")]
    public async Task Customer_ToAgencyMember_NotifiesAgencyOwner_MessageGoesToPublisher()
    {
        var f = new Fixture();

        var result = await f.Handler(f.Customer).Handle(
            new SendMessageCommand(f.Property.Id, null, "Is it still available? secret-text"), CancellationToken.None);

        Assert.Equal(f.Member, result.ReceiverId);          // publisher, not the agency owner
        Assert.Equal(f.Member, f.Saved!.ReceiverId);
        f.Notifications.Verify(x => x.NotifyNewMessageAsync(
            f.Member, f.Customer, It.IsAny<string>(), It.IsAny<Guid>(), f.Property.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Notifications.Verify(x => x.NotifyAgencyMemberInquiryAsync(
            f.AgencyOwner, f.Saved.Id, f.Property.Id, "Sea view flat", "XYZ Real Estate",
            It.Is<string>(c => c.StartsWith("user-")), It.Is<string>(m => m.StartsWith("user-")),
            It.IsAny<CancellationToken>()), Times.Once);
        f.Notifications.Verify(x => x.NotifyAgencyMemberInquiryAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.Is<string>(c => c.Contains("secret-text")), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Individual (non-agency) listing: no agency notification")]
    public async Task Customer_ToIndividual_DoesNotNotifyAnyAgency()
    {
        var f = new Fixture(agencyListing: false);

        await f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "hi"), CancellationToken.None);

        f.Notifications.Verify(x => x.NotifyAgencyMemberInquiryAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Agencies.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Agency owner is the publisher: he receives the message and no second notification is created")]
    public async Task Customer_ToOwnerPublishedListing_NoDuplicateNotification()
    {
        var f = new Fixture(ownerPublishes: true);

        var result = await f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "hi"), CancellationToken.None);

        Assert.Equal(f.AgencyOwner, result.ReceiverId);
        f.Notifications.Verify(x => x.NotifyAgencyMemberInquiryAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Publisher replying to the customer does not notify the agency owner")]
    public async Task PublisherReply_DoesNotNotifyAgencyOwner()
    {
        var f = new Fixture();

        var result = await f.Handler(f.Member).Handle(new SendMessageCommand(f.Property.Id, f.Customer, "yes"), CancellationToken.None);

        Assert.Equal(f.Customer, result.ReceiverId);
        f.Notifications.Verify(x => x.NotifyAgencyMemberInquiryAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "IDOR: a customer-supplied ReceiverId is ignored; the recipient is derived from the listing publisher")]
    public async Task Customer_SuppliedReceiverId_IsIgnored()
    {
        var f = new Fixture();
        var attacker = Guid.NewGuid();

        var result = await f.Handler(f.Customer).Handle(
            new SendMessageCommand(f.Property.Id, attacker, "hi"), CancellationToken.None);

        Assert.Equal(f.Member, result.ReceiverId);
        Assert.NotEqual(attacker, f.Saved!.ReceiverId);
    }

    [Fact(DisplayName = "A non-publisher cannot reply into a conversation that does not exist")]
    public async Task NonPublisherReply_WithoutConversation_Throws()
    {
        var f = new Fixture();
        f.Messages
            .Setup(x => x.ConversationExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Handler(f.Member).Handle(
            new SendMessageCommand(f.Property.Id, Guid.NewGuid(), "hi"), CancellationToken.None));
    }

    [Fact(DisplayName = "A failing agency notification never fails the (already stored) message")]
    public async Task AgencyNotificationFailure_DoesNotFailTheMessage()
    {
        var f = new Fixture();
        f.Notifications
            .Setup(x => x.NotifyAgencyMemberInquiryAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push down"));

        var result = await f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "hi"), CancellationToken.None);

        Assert.Equal(f.Member, result.ReceiverId);
    }

    // ---- P8-D07: per-listing sending limits and notification coalescing -----------------------

    [Fact(DisplayName = "P8-D07: the 11th message to one listing within ten minutes is refused with a 429 rule and nothing is stored")]
    public async Task Customer_AboveTheBurstLimit_IsRefused()
    {
        var f = new Fixture(agencyListing: false);
        f.Messages
            .Setup(x => x.CountSentAboutPropertySinceAsync(f.Property.Id, f.Customer, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MessagingLimits.MaxMessagesPerBurstWindow);

        var error = await Assert.ThrowsAsync<TooManyRequestsException>(() => f.Handler(f.Customer).Handle(
            new SendMessageCommand(f.Property.Id, null, "again"), CancellationToken.None));

        Assert.Equal("MESSAGE_RATE_LIMIT", error.Code);
        Assert.Null(f.Saved);
        f.Notifications.Verify(x => x.NotifyNewMessageAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "P8-D07: the 10th message inside the window still goes through")]
    public async Task Customer_JustUnderTheBurstLimit_IsAccepted()
    {
        var f = new Fixture(agencyListing: false);
        f.Messages
            .Setup(x => x.CountSentAboutPropertySinceAsync(f.Property.Id, f.Customer, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MessagingLimits.MaxMessagesPerBurstWindow - 1);

        await f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "ok"), CancellationToken.None);

        Assert.NotNull(f.Saved);
    }

    [Fact(DisplayName = "P8-D07: a slow drip is stopped by the daily cap")]
    public async Task Customer_AboveTheDailyLimit_IsRefused()
    {
        var f = new Fixture(agencyListing: false);
        f.Messages
            .Setup(x => x.CountSentAboutPropertySinceAsync(f.Property.Id, f.Customer, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, DateTime since, CancellationToken _) =>
                since > DateTime.UtcNow.AddHours(-1) ? 0 : MessagingLimits.MaxMessagesPerDay);

        var error = await Assert.ThrowsAsync<TooManyRequestsException>(() => f.Handler(f.Customer).Handle(
            new SendMessageCommand(f.Property.Id, null, "drip"), CancellationToken.None));

        Assert.Equal("MESSAGE_DAILY_LIMIT", error.Code);
        Assert.Null(f.Saved);
    }

    [Fact(DisplayName = "P8-D07: while an earlier unread message from the same sender is fresh, no second notification is raised, but the message is stored")]
    public async Task Customer_FollowUpWhileTheFirstIsUnread_StoresTheMessageWithoutANewNotification()
    {
        var f = new Fixture();
        f.Messages
            .Setup(x => x.HasRecentUnreadAsync(f.Property.Id, f.Customer, f.Member, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "second"), CancellationToken.None);

        Assert.NotNull(f.Saved);
        f.Notifications.Verify(x => x.NotifyNewMessageAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Notifications.Verify(x => x.NotifyAgencyMemberInquiryAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
