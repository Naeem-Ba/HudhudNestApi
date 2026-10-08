using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Agencies.Commands.RemoveAgencyMember;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.CreateProperty;
using HudhudNestApi.Application.Listings.Commands.PublishProperty;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Application.Users.Messaging.Commands.SendMessage;
using HudhudNestApi.Application.Users.Messaging.Interfaces;
using HudhudNestApi.Application.Users.Messaging.Queries.GetConversations;
using HudhudNestApi.Application.Users.Messaging.Queries.GetMessages;
using HudhudNestApi.Application.Users.Messaging.DTOs;
using HudhudNestApi.Application.Users.Models;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Messaging.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Agencies;

/// <summary>
/// Business rules decided for the agency-member workflow:
/// member leaves -> listings become individual; deactivated agency blocks new listings and
/// publishing; disabled publisher -> inquiries go to the agency owner; the agency owner can read
/// (not write) the conversations about their agency's listings; the owner is also the manager.
/// </summary>
public sealed class AgencyWorkflowRulesTests
{
    private static Agency NewAgency(Guid ownerId) =>
        Agency.Create("XYZ Real Estate", "xyz", ownerId, "SY", DateTime.UtcNow);

    private static Property AgencyListing(Guid publisherId, Agency agency)
    {
        var property = Property.Create("Flat", "A description", publisherId, ListingType.ForRent);
        property.SetAgency(agency.Id);
        return property;
    }

    // ---- member leaves -> listings become individual ----------------------------------------

    [Fact(DisplayName = "Removing a member clears the agency attribution of the member's listings before saving")]
    public async Task RemoveMember_ClearsMemberListingAttribution()
    {
        var ownerId = Guid.NewGuid();
        var agency = NewAgency(ownerId);
        var member = UserAccount.Create(Guid.NewGuid(), "Omar", "M", DateTime.UtcNow);
        member.JoinAgency(agency.Id, DateTime.UtcNow);

        var order = new List<string>();
        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        agencies.Setup(x => x.GetUserAccountAsync(member.Id, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        agencies.Setup(x => x.ClearMemberAttributionAsync(agency.Id, member.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("clear")).ReturnsAsync(2);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).ReturnsAsync(1);
        var identity = new Mock<IAdminIdentityService>();
        identity.Setup(x => x.RemoveRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("ok"));

        await new RemoveAgencyMemberCommandHandler(agencies.Object, identity.Object, uow.Object,
                NullLogger<RemoveAgencyMemberCommandHandler>.Instance)
            .Handle(new RemoveAgencyMemberCommand(agency.Id, member.Id, ownerId, null), CancellationToken.None);

        Assert.Equal(["clear", "save"], order);
        Assert.Null(member.AgencyId);
    }

    // ---- deactivated agency blocks publishing -----------------------------------------------

    private static CreatePropertyCommand CreateCommand(Guid ownerId) => new(
        ownerId, "T", "D", ListingType.ForRent, "S", "C", null, "SY", null,
        1, 1, null, null, null, 1, null, null, 100m, null, null, null, null, "USD",
        2, 50m, 1, false, false, false, HeatingType.Unknown, null, null);

    [Fact(DisplayName = "A member of a deactivated agency cannot create a listing (403 AGENCY_INACTIVE)")]
    public async Task Create_ForMemberOfDeactivatedAgency_IsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var agency = NewAgency(Guid.NewGuid());
        agency.Deactivate(DateTime.UtcNow);
        var member = UserAccount.Create(ownerId, "Omar", "M", DateTime.UtcNow);
        member.JoinAgency(agency.Id, DateTime.UtcNow);
        member.SelectPlan(Guid.NewGuid(), DateTime.UtcNow);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        var identity = new Mock<IUserIdentityReadService>();
        identity.Setup(x => x.FindByIdAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountSnapshot(Guid.NewGuid(), ownerId, null, "+1", false, true, true, false));
        var repo = new Mock<IPropertyRepository>();

        var handler = new CreatePropertyCommandHandler(
            repo.Object, agencies.Object, identity.Object, Mock.Of<IUnitOfWork>(),
            Mock.Of<ILocationSuggestionService>(), Mock.Of<IListingQuotaPolicy>(),
            Mock.Of<IActiveListingCounter>(), Mock.Of<IPublisher>(),
            NullLogger<CreatePropertyCommandHandler>.Instance);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(CreateCommand(ownerId), CancellationToken.None));

        Assert.Equal("AGENCY_INACTIVE", ex.Code);
        repo.Verify(x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PublishPropertyCommandHandler PublishHandler(Property property, Agency agency, Mock<IUnitOfWork> uow)
    {
        var repository = new Mock<IPropertyRepository>();
        repository.Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new PublishPropertyCommandHandler(repository.Object, uow.Object, Mock.Of<IPublisher>(), agencies.Object);
    }

    [Fact(DisplayName = "Publishing an agency listing requires an active agency (non-admin); admins are exempt")]
    public async Task Publish_AgencyListing_RequiresActiveAgency()
    {
        var agency = NewAgency(Guid.NewGuid());
        agency.Deactivate(DateTime.UtcNow);
        var member = Guid.NewGuid();
        var property = AgencyListing(member, agency);
        property.Images.Add(new PropertyImage { Url = "u", PublicId = "p", IsMain = true });
        property.Unpublish();

        var uow = new Mock<IUnitOfWork>();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            PublishHandler(property, agency, uow).Handle(new PublishPropertyCommand(property.Id, member, IsAdmin: false), CancellationToken.None));
        Assert.Equal("AGENCY_INACTIVE", ex.Code);
        Assert.False(property.IsPublished);

        await PublishHandler(property, agency, uow).Handle(new PublishPropertyCommand(property.Id, member, IsAdmin: true), CancellationToken.None);
        Assert.True(property.IsPublished);
    }

    // ---- disabled publisher -> agency owner answers ------------------------------------------

    private sealed class MessageFixture
    {
        public Guid Customer { get; } = Guid.NewGuid();
        public Guid Member { get; } = Guid.NewGuid();
        public Guid AgencyOwner { get; } = Guid.NewGuid();
        public Agency Agency { get; }
        public Property Property { get; }
        public Message? Saved { get; private set; }
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IUserDirectoryReadService> Directory { get; } = new();

        public MessageFixture(bool memberActive)
        {
            Agency = NewAgency(AgencyOwner);
            Property = AgencyListing(Member, Agency);
            Directory.Setup(x => x.GetActiveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) =>
                    id == Member && !memberActive ? null : new UserDirectoryEntry(id, "user-" + id.ToString()[..4], null));
        }

        public SendMessageCommandHandler Handler(Guid asUser)
        {
            var current = new Mock<ICurrentUserService>();
            current.SetupGet(x => x.UserId).Returns(asUser);
            var properties = new Mock<IPropertyRepository>();
            properties.Setup(x => x.GetByIdWithDetailsAsync(Property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Property);
            var messages = new Mock<IMessageRepository>();
            messages.Setup(x => x.AddAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Callback<Message, CancellationToken>((m, _) => Saved = m).Returns(Task.CompletedTask);
            messages.Setup(x => x.ConversationExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var agencies = new Mock<IAgencyRepository>();
            agencies.Setup(x => x.GetByIdAsync(Agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Agency);
            var uow = new Mock<IUnitOfWork>();
            uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            return new SendMessageCommandHandler(current.Object, properties.Object, Directory.Object, messages.Object,
                uow.Object, Notifications.Object, agencies.Object, NullLogger<SendMessageCommandHandler>.Instance);
        }
    }

    [Fact(DisplayName = "Disabled publisher + active agency: the customer's inquiry goes to the agency owner (no extra agency notice)")]
    public async Task DisabledPublisher_InquiryGoesToAgencyOwner()
    {
        var f = new MessageFixture(memberActive: false);

        var result = await f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "hi"), CancellationToken.None);

        Assert.Equal(f.AgencyOwner, result.ReceiverId);
        f.Notifications.Verify(x => x.NotifyNewMessageAsync(f.AgencyOwner, f.Customer, It.IsAny<string>(), It.IsAny<Guid>(), f.Property.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Notifications.Verify(x => x.NotifyAgencyMemberInquiryAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Disabled publisher: the agency owner can reply to the customer in the same thread")]
    public async Task DisabledPublisher_AgencyOwnerCanReply()
    {
        var f = new MessageFixture(memberActive: false);

        var result = await f.Handler(f.AgencyOwner).Handle(new SendMessageCommand(f.Property.Id, f.Customer, "yes"), CancellationToken.None);

        Assert.Equal(f.Customer, result.ReceiverId);
    }

    [Fact(DisplayName = "Active publisher: nothing is re-routed to the agency owner")]
    public async Task ActivePublisher_NoRouting()
    {
        var f = new MessageFixture(memberActive: true);

        var result = await f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "hi"), CancellationToken.None);

        Assert.Equal(f.Member, result.ReceiverId);
    }

    [Fact(DisplayName = "Disabled publisher and deactivated agency: no routing (publisher stays the receiver and the existing 404 applies)")]
    public async Task DisabledPublisher_InactiveAgency_NoRouting()
    {
        var f = new MessageFixture(memberActive: false);
        f.Agency.Deactivate(DateTime.UtcNow);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Handler(f.Customer).Handle(new SendMessageCommand(f.Property.Id, null, "hi"), CancellationToken.None));
    }

    // ---- agency owner reads members' conversations -------------------------------------------

    private static (GetMessagesQueryHandler Handler, Mock<IMessageRepository> Messages) ReadHandler(Property property, Agency agency, Guid asUser)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.UserId).Returns(asUser);
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        var messages = new Mock<IMessageRepository>();
        messages.Setup(x => x.GetConversationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Message> { Items = [], TotalCount = 0, Page = 1, PageSize = 20 });
        messages.Setup(x => x.GetByPropertyAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Message> { Items = [], TotalCount = 0, Page = 1, PageSize = 20 });
        return (new GetMessagesQueryHandler(current.Object, properties.Object, messages.Object, agencies.Object), messages);
    }

    [Fact(DisplayName = "Agency owner can read a member's thread (publisher <-> customer) and list all messages of the agency listing")]
    public async Task AgencyOwner_CanReadMembersConversations()
    {
        var ownerId = Guid.NewGuid();
        var agency = NewAgency(ownerId);
        var member = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var property = AgencyListing(member, agency);
        var (handler, messages) = ReadHandler(property, agency, ownerId);

        await handler.Handle(new GetMessagesQuery(property.Id, customer), CancellationToken.None);
        await handler.Handle(new GetMessagesQuery(property.Id, null), CancellationToken.None);

        // the thread read is the publisher's, not the owner's own
        messages.Verify(x => x.GetConversationAsync(property.Id, member, customer, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        messages.Verify(x => x.GetByPropertyAsync(property.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "A stranger and the owner of ANOTHER agency cannot list the messages of an agency listing")]
    public async Task OtherUsers_CannotListAgencyListingMessages()
    {
        var agency = NewAgency(Guid.NewGuid());
        var property = AgencyListing(Guid.NewGuid(), agency);
        var otherAgency = NewAgency(Guid.NewGuid());

        foreach (var user in new[] { Guid.NewGuid(), otherAgency.OwnerUserId })
        {
            var (handler, _) = ReadHandler(property, agency, user);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.Handle(new GetMessagesQuery(property.Id, null), CancellationToken.None));
        }
    }

    [Fact(DisplayName = "Agency owner's conversation inbox for an agency listing shows the publisher's conversations; other listings stay own-inbox")]
    public async Task AgencyOwner_InboxForAgencyListing_IsPublishersInbox()
    {
        var ownerId = Guid.NewGuid();
        var agency = NewAgency(ownerId);
        var member = Guid.NewGuid();
        var property = AgencyListing(member, agency);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.UserId).Returns(ownerId);
        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(property);
        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        var messages = new Mock<IMessageRepository>();
        messages.Setup(x => x.GetConversationSummariesAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ConversationSummaryDto> { Items = [], TotalCount = 0, Page = 1, PageSize = 20 });
        var handler = new GetConversationsQueryHandler(current.Object, messages.Object, agencies.Object, properties.Object);

        await handler.Handle(new GetConversationsQuery(property.Id), CancellationToken.None);
        await handler.Handle(new GetConversationsQuery(null), CancellationToken.None);

        messages.Verify(x => x.GetConversationSummariesAsync(member, property.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        messages.Verify(x => x.GetConversationSummariesAsync(ownerId, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
