using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.Commands.AcceptAgencyInvitation;
using PropertyApi.Application.Agencies.Commands.CreateAgencyInvitation;
using PropertyApi.Application.Agencies.Commands.DeclineAgencyInvitation;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Agencies.Enums;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Tests.Agencies;

/// <summary>
/// Covers B-2's replacement for direct membership attachment (RELEASE-BLOCKERS-AR.md):
/// CreateAgencyInvitation only ever creates a Pending row, and AcceptAgencyInvitation is
/// the only path that calls UserAccount.JoinAgency. Weighted toward the boundaries the
/// consent model depends on — who may invite, who may respond, and that a non-Pending
/// invitation can never be responded to again.
/// </summary>
public sealed class AgencyInvitationTests
{
    // ── Create invitation ────────────────────────────────────────

    [Fact]
    public async Task Create_ByTheOwner_ProducesAPendingInvitation_AndNoMembership()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);
        var target = BuildAccount();

        var agencies = BuildAgencyRepository(agency, owner, target);
        var invitations = BuildInvitationRepository();
        var notifications = new Mock<INotificationService>();

        var handler = CreateHandler(agencies, invitations, notifications);

        var dto = await handler.Handle(
            new CreateAgencyInvitationCommand(agency.Id, target.Id, ownerId, null),
            CancellationToken.None);

        Assert.Equal(AgencyInvitationStatus.Pending, dto.Status);
        Assert.Equal(target.Id, dto.TargetUserId);

        // The whole point of B-2's fix: creating the invitation must never touch AgencyId.
        Assert.Null(target.AgencyId);

        notifications.Verify(
            x => x.NotifyAgencyInvitationReceivedAsync(
                target.Id, It.IsAny<Guid>(), agency.Name, It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_BySomeoneWhoIsNotThisAgencysOwner_IsForbidden()
    {
        var agency = BuildAgency(Guid.NewGuid());
        var target = BuildAccount();

        var agencies = BuildAgencyRepository(agency, BuildAccount(), target);
        var handler = CreateHandler(agencies, BuildInvitationRepository(), new Mock<INotificationService>());

        // Holding AgencyOwner means you own *an* agency, not *this* one.
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new CreateAgencyInvitationCommand(agency.Id, target.Id, Guid.NewGuid(), null),
            CancellationToken.None));

        Assert.Null(target.AgencyId);
    }

    [Fact]
    public async Task Create_ForANonexistentUser_IsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), targetUser: null);

        var handler = CreateHandler(agencies, BuildInvitationRepository(), new Mock<INotificationService>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateAgencyInvitationCommand(agency.Id, Guid.NewGuid(), ownerId, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_ForAnExistingMember_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        target.JoinAgency(agency.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = CreateHandler(agencies, BuildInvitationRepository(), new Mock<INotificationService>());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateAgencyInvitationCommand(agency.Id, target.Id, ownerId, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_WhenAPendingInvitationAlreadyExistsForThisPair_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var invitations = BuildInvitationRepository();
        var existing = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);
        invitations
            .Setup(x => x.GetPendingAsync(agency.Id, target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = CreateHandler(agencies, invitations, new Mock<INotificationService>());

        // Not expired — MarkExpiredIfDue is a no-op, so this is a live duplicate.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateAgencyInvitationCommand(agency.Id, target.Id, ownerId, null),
            CancellationToken.None));

        invitations.Verify(
            x => x.AddAsync(It.IsAny<AgencyInvitation>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_WhenTheOnlyExistingInvitationHasExpired_IsAllowed()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var invitations = BuildInvitationRepository();

        // Created 20 days ago with the 14-day default window — already past ExpiresAt.
        var stale = AgencyInvitation.Create(
            agency.Id, ownerId, target.Id, DateTime.UtcNow.AddDays(-20));
        invitations
            .Setup(x => x.GetPendingAsync(agency.Id, target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);

        var handler = CreateHandler(agencies, invitations, new Mock<INotificationService>());

        var dto = await handler.Handle(
            new CreateAgencyInvitationCommand(agency.Id, target.Id, ownerId, null),
            CancellationToken.None);

        Assert.Equal(AgencyInvitationStatus.Pending, dto.Status);
        Assert.Equal(AgencyInvitationStatus.Expired, stale.Status);
    }

    // ── Accept ───────────────────────────────────────────────────

    [Fact]
    public async Task Accept_ByTheTargetUser_CreatesTheMembership_AndGrantsTheAgentRole()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var invitations = BuildInvitationRepository(invitation);
        var identity = BuildIdentity();
        var notifications = new Mock<INotificationService>();

        var handler = AcceptHandler(agencies, invitations, identity, notifications);

        var member = await handler.Handle(
            new AcceptAgencyInvitationCommand(invitation.Id, target.Id, null),
            CancellationToken.None);

        Assert.Equal(agency.Id, target.AgencyId);
        Assert.Equal(AgencyInvitationStatus.Accepted, invitation.Status);
        Assert.NotNull(invitation.RespondedAt);
        Assert.False(member.IsOwner);

        identity.Verify(
            x => x.AssignRoleAsync(
                target.Id, RoleNames.AgencyAgent, target.Id,
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        notifications.Verify(
            x => x.NotifyAgencyInvitationRespondedAsync(
                ownerId, invitation.Id, It.IsAny<string>(), true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Accept_ByAUserTheInvitationWasNotSentTo_IsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = AcceptHandler(
            agencies, BuildInvitationRepository(invitation), BuildIdentity(), new Mock<INotificationService>());

        // Not the inviter, not the target — an unrelated caller who merely knows the id.
        var impostor = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AcceptAgencyInvitationCommand(invitation.Id, impostor, null),
            CancellationToken.None));

        Assert.Null(target.AgencyId);
    }

    [Fact]
    public async Task Accept_TheInviter_IsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = AcceptHandler(
            agencies, BuildInvitationRepository(invitation), BuildIdentity(), new Mock<INotificationService>());

        // The owner who sent it cannot accept on the target's behalf.
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AcceptAgencyInvitationCommand(invitation.Id, ownerId, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Accept_Twice_IsRejected_AndCreatesOnlyOneMembership()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var invitations = BuildInvitationRepository(invitation);
        var identity = BuildIdentity();

        var handler = AcceptHandler(agencies, invitations, identity, new Mock<INotificationService>());
        var command = new AcceptAgencyInvitationCommand(invitation.Id, target.Id, null);

        await handler.Handle(command, CancellationToken.None);

        // Simulates the second of two near-simultaneous Accept calls: by the time this one
        // re-reads the row (ReloadAsync, after acquiring the same advisory-lock key the
        // first call used), Status is already Accepted.
        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(command, CancellationToken.None));

        identity.Verify(
            x => x.AssignRoleAsync(
                target.Id, RoleNames.AgencyAgent, It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Accept_ADeclinedInvitation_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);
        invitation.Decline(DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = AcceptHandler(
            agencies, BuildInvitationRepository(invitation), BuildIdentity(), new Mock<INotificationService>());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new AcceptAgencyInvitationCommand(invitation.Id, target.Id, null),
            CancellationToken.None));

        Assert.Null(target.AgencyId);
    }

    [Fact]
    public async Task Accept_AnExpiredInvitation_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(
            agency.Id, ownerId, target.Id, DateTime.UtcNow.AddDays(-20));

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = AcceptHandler(
            agencies, BuildInvitationRepository(invitation), BuildIdentity(), new Mock<INotificationService>());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new AcceptAgencyInvitationCommand(invitation.Id, target.Id, null),
            CancellationToken.None));

        Assert.Null(target.AgencyId);
        Assert.Equal(AgencyInvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public async Task Accept_WhenTheTargetJoinedAnotherAgencyMeanwhile_IsRejected()
    {
        // Scenario E: the user joined a different agency through some other path while
        // this invitation sat Pending.
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var otherAgencyId = Guid.NewGuid();
        target.JoinAgency(otherAgencyId, DateTime.UtcNow);
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = AcceptHandler(
            agencies, BuildInvitationRepository(invitation), BuildIdentity(), new Mock<INotificationService>());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new AcceptAgencyInvitationCommand(invitation.Id, target.Id, null),
            CancellationToken.None));

        // JoinAgency is never reached for the new agency — the membership the user already
        // had stands untouched.
        Assert.Equal(otherAgencyId, target.AgencyId);
    }

    // ── Decline ──────────────────────────────────────────────────

    [Fact]
    public async Task Decline_ByTheTargetUser_CreatesNoMembership()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var notifications = new Mock<INotificationService>();
        var handler = DeclineHandler(agencies, BuildInvitationRepository(invitation), notifications);

        await handler.Handle(
            new DeclineAgencyInvitationCommand(invitation.Id, target.Id),
            CancellationToken.None);

        Assert.Equal(AgencyInvitationStatus.Declined, invitation.Status);
        Assert.Null(target.AgencyId);

        notifications.Verify(
            x => x.NotifyAgencyInvitationRespondedAsync(
                ownerId, invitation.Id, It.IsAny<string>(), false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Decline_ByAUserTheInvitationWasNotSentTo_IsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = DeclineHandler(
            agencies, BuildInvitationRepository(invitation), new Mock<INotificationService>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new DeclineAgencyInvitationCommand(invitation.Id, Guid.NewGuid()),
            CancellationToken.None));

        Assert.Equal(AgencyInvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public async Task Decline_AnAlreadyAcceptedInvitation_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = BuildAccount();
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);
        invitation.Accept(DateTime.UtcNow);
        target.JoinAgency(agency.Id, DateTime.UtcNow);

        var agencies = BuildAgencyRepository(agency, BuildAccount(ownerId), target);
        var handler = DeclineHandler(
            agencies, BuildInvitationRepository(invitation), new Mock<INotificationService>());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new DeclineAgencyInvitationCommand(invitation.Id, target.Id),
            CancellationToken.None));

        // Declining after acceptance must not unwind the membership it already granted.
        Assert.Equal(agency.Id, target.AgencyId);
    }

    // ── Domain: state machine ────────────────────────────────────

    [Fact]
    public void Create_InvitingYourself_Throws()
    {
        var userId = Guid.NewGuid();

        Assert.Throws<DomainException>(() => AgencyInvitation.Create(
            Guid.NewGuid(), userId, userId, DateTime.UtcNow));
    }

    [Fact]
    public void Accept_ThenAccept_Throws()
    {
        var invitation = AgencyInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        invitation.Accept(DateTime.UtcNow);

        Assert.Throws<DomainException>(() => invitation.Accept(DateTime.UtcNow));
    }

    [Fact]
    public void Accept_ThenDecline_Throws()
    {
        var invitation = AgencyInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        invitation.Accept(DateTime.UtcNow);

        Assert.Throws<DomainException>(() => invitation.Decline(DateTime.UtcNow));
    }

    [Fact]
    public void Decline_ThenAccept_Throws()
    {
        var invitation = AgencyInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        invitation.Decline(DateTime.UtcNow);

        Assert.Throws<DomainException>(() => invitation.Accept(DateTime.UtcNow));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Agency BuildAgency(Guid ownerId) => Agency.Create(
        "مكتب الشام العقاري", "sham-realty", ownerId, "SY", DateTime.UtcNow);

    private static UserAccount BuildAccount(Guid? id = null) => UserAccount.Create(
        id ?? Guid.NewGuid(), "نعيم", "بزازة", DateTime.UtcNow);

    private static Mock<IAgencyRepository> BuildAgencyRepository(
        Agency agency,
        UserAccount owner,
        UserAccount? targetUser)
    {
        var repo = new Mock<IAgencyRepository>();

        repo.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        repo.Setup(x => x.GetUserAccountAsync(owner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);
        repo.Setup(x => x.CountMembersAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        if (targetUser is not null)
        {
            repo.Setup(x => x.GetUserAccountAsync(targetUser.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(targetUser);
        }

        return repo;
    }

    private static Mock<IAgencyInvitationRepository> BuildInvitationRepository(
        AgencyInvitation? existingById = null)
    {
        var repo = new Mock<IAgencyInvitationRepository>();

        repo.Setup(x => x.GetPendingAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgencyInvitation?)null);

        // No real database in a unit test — ReloadAsync is a no-op, and every handler
        // mutates the exact same in-memory instance GetByIdAsync/AddAsync already returned.
        repo.Setup(x => x.ReloadAsync(It.IsAny<AgencyInvitation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        if (existingById is not null)
        {
            repo.Setup(x => x.GetByIdAsync(existingById.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingById);
        }

        return repo;
    }

    private static Mock<IAdminIdentityService> BuildIdentity()
    {
        var identity = new Mock<IAdminIdentityService>();

        identity
            .Setup(x => x.AssignRoleAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("assigned"));

        return identity;
    }

    private static CreateAgencyInvitationCommandHandler CreateHandler(
        Mock<IAgencyRepository> agencies,
        Mock<IAgencyInvitationRepository> invitations,
        Mock<INotificationService> notifications)
        => new(
            agencies.Object,
            invitations.Object,
            Mock.Of<IUnitOfWork>(),
            notifications.Object,
            NullLogger<CreateAgencyInvitationCommandHandler>.Instance);

    private static AcceptAgencyInvitationCommandHandler AcceptHandler(
        Mock<IAgencyRepository> agencies,
        Mock<IAgencyInvitationRepository> invitations,
        Mock<IAdminIdentityService> identity,
        Mock<INotificationService> notifications)
        => new(
            agencies.Object,
            invitations.Object,
            identity.Object,
            Mock.Of<IUnitOfWork>(),
            notifications.Object,
            NullLogger<AcceptAgencyInvitationCommandHandler>.Instance);

    private static DeclineAgencyInvitationCommandHandler DeclineHandler(
        Mock<IAgencyRepository> agencies,
        Mock<IAgencyInvitationRepository> invitations,
        Mock<INotificationService> notifications)
        => new(
            agencies.Object,
            invitations.Object,
            Mock.Of<IUnitOfWork>(),
            notifications.Object,
            NullLogger<DeclineAgencyInvitationCommandHandler>.Instance);
}
