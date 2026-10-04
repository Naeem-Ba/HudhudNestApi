using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitation;
using HudhudNestApi.Application.Agencies.Commands.CreateAgencyInvitationByContact;
using HudhudNestApi.Application.Agencies.Commands.ReactivateAgency;
using HudhudNestApi.Application.Agencies.DTOs;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Queries.GetAgencySentInvitations;
using HudhudNestApi.Application.Agencies.Validators;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Agencies.Enums;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Agencies;

/// <summary>
/// The owner tools that closed the agency UI gaps: invite by e-mail/phone, reactivate, and
/// the owner's list of invitations still waiting for an answer.
/// </summary>
public sealed class AgencyOwnerToolsTests
{
    // ── Invite by contact ────────────────────────────────────────

    [Fact]
    public async Task ByContact_Email_ResolvesTheAccount_AndDelegatesToTheIdInvitation()
    {
        var agencyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var emails = new Mock<ILoginIdentityService>();
        emails.Setup(x => x.FindByEmailAsync("sara@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(targetId));

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<CreateAgencyInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvitationDto(agencyId, targetId));

        var handler = ByContactHandler(emails, new Mock<IPhoneOtpIdentityService>(), sender);

        await handler.Handle(
            new CreateAgencyInvitationByContactCommand(agencyId, "  Sara@Example.com ", null, ownerId, null),
            CancellationToken.None);

        sender.Verify(x => x.Send(
            It.Is<CreateAgencyInvitationCommand>(c =>
                c.AgencyId == agencyId && c.TargetUserId == targetId && c.RequestingUserId == ownerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ByContact_Phone_IsNormalisedBeforeTheLookup()
    {
        var targetId = Guid.NewGuid();
        var phones = new Mock<IPhoneOtpIdentityService>();
        phones.Setup(x => x.FindByPhoneNumberAsync("+963911111111", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(targetId));

        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(It.IsAny<CreateAgencyInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvitationDto(Guid.NewGuid(), targetId));

        var handler = ByContactHandler(new Mock<ILoginIdentityService>(), phones, sender);

        await handler.Handle(
            new CreateAgencyInvitationByContactCommand(Guid.NewGuid(), null, "0911 111 111", Guid.NewGuid(), null),
            CancellationToken.None);

        sender.Verify(x => x.Send(
            It.Is<CreateAgencyInvitationCommand>(c => c.TargetUserId == targetId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ByContact_UnknownOrDeletedAccount_IsNotFound_AndNothingIsSent()
    {
        var deletedId = Guid.NewGuid();
        var emails = new Mock<ILoginIdentityService>();
        emails.Setup(x => x.FindByEmailAsync("gone@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(deletedId, isDeleted: true));

        var sender = new Mock<ISender>();
        var handler = ByContactHandler(emails, new Mock<IPhoneOtpIdentityService>(), sender);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateAgencyInvitationByContactCommand(Guid.NewGuid(), "nobody@example.com", null, Guid.NewGuid(), null),
            CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new CreateAgencyInvitationByContactCommand(Guid.NewGuid(), "gone@example.com", null, Guid.NewGuid(), null),
            CancellationToken.None));

        sender.Verify(x => x.Send(It.IsAny<CreateAgencyInvitationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ByContact_InvalidPhone_IsAValidationError()
    {
        var handler = ByContactHandler(
            new Mock<ILoginIdentityService>(), new Mock<IPhoneOtpIdentityService>(), new Mock<ISender>());

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new CreateAgencyInvitationByContactCommand(Guid.NewGuid(), null, "abc", Guid.NewGuid(), null),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("a@example.com", "0911111111", false)]
    [InlineData("not-an-email", null, false)]
    [InlineData("a@example.com", null, true)]
    [InlineData(null, "0911111111", true)]
    public void ByContact_Validator_RequiresExactlyOneContact(string? email, string? phone, bool valid)
    {
        var result = new CreateAgencyInvitationByContactCommandValidator().Validate(
            new CreateAgencyInvitationByContactCommand(Guid.NewGuid(), email, phone, Guid.NewGuid(), null));

        Assert.Equal(valid, result.IsValid);
    }

    // ── Reactivate ───────────────────────────────────────────────

    [Fact]
    public async Task Reactivate_ByTheOwner_TurnsTheAgencyBackOn()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        agency.Deactivate(DateTime.UtcNow);

        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new ReactivateAgencyCommandHandler(
            AgencyRepository(agency).Object, unitOfWork.Object, NullLogger<ReactivateAgencyCommandHandler>.Instance);

        await handler.Handle(new ReactivateAgencyCommand(agency.Id, ownerId), CancellationToken.None);

        Assert.True(agency.IsActive);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reactivate_BySomeoneElse_IsForbidden_AndTheAgencyStaysOff()
    {
        var agency = BuildAgency(Guid.NewGuid());
        agency.Deactivate(DateTime.UtcNow);

        var handler = new ReactivateAgencyCommandHandler(
            AgencyRepository(agency).Object, Mock.Of<IUnitOfWork>(), NullLogger<ReactivateAgencyCommandHandler>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new ReactivateAgencyCommand(agency.Id, Guid.NewGuid()), CancellationToken.None));

        Assert.False(agency.IsActive);
    }

    [Fact]
    public async Task Reactivate_AnActiveAgency_IsANoOp()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new ReactivateAgencyCommandHandler(
            AgencyRepository(agency).Object, unitOfWork.Object, NullLogger<ReactivateAgencyCommandHandler>.Instance);

        await handler.Handle(new ReactivateAgencyCommand(agency.Id, ownerId), CancellationToken.None);

        Assert.True(agency.IsActive);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Sent invitations ─────────────────────────────────────────

    [Fact]
    public async Task SentInvitations_ForTheOwner_ListsPendingInvitationsWithTheTargetsName()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var target = UserAccount.Create(Guid.NewGuid(), "سارة", "خليل", DateTime.UtcNow);
        var invitation = AgencyInvitation.Create(agency.Id, ownerId, target.Id, DateTime.UtcNow);

        var agencies = AgencyRepository(agency);
        agencies.Setup(x => x.GetUserAccountAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var invitations = new Mock<IAgencyInvitationRepository>();
        invitations.Setup(x => x.GetActionableForAgencyAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { invitation });

        var result = await new GetAgencySentInvitationsQueryHandler(agencies.Object, invitations.Object)
            .Handle(new GetAgencySentInvitationsQuery(agency.Id, ownerId), CancellationToken.None);

        var row = Assert.Single(result);
        Assert.Equal(invitation.Id, row.Id);
        Assert.Equal(target.Id, row.TargetUserId);
        Assert.Equal("سارة خليل", row.TargetName);
    }

    [Fact]
    public async Task SentInvitations_ForAMember_IsForbidden()
    {
        var agency = BuildAgency(Guid.NewGuid());
        var handler = new GetAgencySentInvitationsQueryHandler(
            AgencyRepository(agency).Object, new Mock<IAgencyInvitationRepository>().Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetAgencySentInvitationsQuery(agency.Id, Guid.NewGuid()), CancellationToken.None));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Agency BuildAgency(Guid ownerId) => Agency.Create(
        "مكتب الشام العقاري", "sham-realty", ownerId, "SY", DateTime.UtcNow);

    private static Mock<IAgencyRepository> AgencyRepository(Agency agency)
    {
        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        return repo;
    }

    private static IdentityAccountSnapshot Snapshot(Guid id, bool isDeleted = false) =>
        new(id, id, "x@example.com", null, true, false, true, isDeleted);

    private static AgencyInvitationDto InvitationDto(Guid agencyId, Guid targetId) =>
        new(Guid.NewGuid(), agencyId, "مكتب", "sham-realty", Guid.NewGuid(), "نعيم", targetId,
            AgencyInvitationStatus.Pending, DateTime.UtcNow, DateTime.UtcNow.AddDays(14), null);

    private static CreateAgencyInvitationByContactCommandHandler ByContactHandler(
        Mock<ILoginIdentityService> emails,
        Mock<IPhoneOtpIdentityService> phones,
        Mock<ISender> sender)
    {
        var normalizer = new Mock<IPhoneNumberNormalizer>();
        normalizer.Setup(x => x.Normalize(It.IsAny<string?>()))
            .Returns((string? raw) => raw is not null && raw.Any(char.IsDigit)
                ? PhoneNumberNormalizationResult.Success("+963911111111")
                : PhoneNumberNormalizationResult.Invalid());

        return new CreateAgencyInvitationByContactCommandHandler(
            emails.Object, phones.Object, normalizer.Object, sender.Object);
    }
}
