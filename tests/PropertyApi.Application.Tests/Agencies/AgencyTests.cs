using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.Commands.CreateAgency;
using PropertyApi.Application.Agencies.Commands.DeactivateAgency;
using PropertyApi.Application.Agencies.Commands.RemoveAgencyMember;
using PropertyApi.Application.Agencies.Commands.SetAgencyLogo;
using PropertyApi.Application.Agencies.Commands.TransferAgencyOwnership;
using PropertyApi.Application.Agencies.Commands.UpdateAgency;
using PropertyApi.Application.Agencies.DTOs;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Agencies.Queries.GetAgencyBySlug;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Tests.Agencies;

/// <summary>
/// Covers the agency grouping: slug normalisation, one-agency-per-user, and the membership
/// rules that decide who may attach or detach whom.
///
/// Weighted toward the boundaries the chosen model leans on. Agency is an organisational
/// grouping with no row-level isolation, so the guards that matter are the ones that keep
/// an owner's authority scoped to their own agency, and the ones that stop membership
/// changes from reaching into a member's listings.
/// </summary>
public sealed class AgencyTests
{
    // ── Domain: creation and slugs ───────────────────────────────

    [Fact]
    public void Create_WithoutOwner_Throws()
    {
        // An agency with no owner has nobody who can administer it.
        Assert.Throws<DomainException>(() => Agency.Create(
            "مكتب الشام العقاري",
            "sham-realty",
            Guid.Empty,
            "SY",
            DateTime.UtcNow));
    }

    [Fact]
    public void Create_WithInvalidCountryCode_Throws()
    {
        Assert.Throws<DomainException>(() => Agency.Create(
            "مكتب الشام العقاري",
            "sham-realty",
            Guid.NewGuid(),
            "SYR",
            DateTime.UtcNow));
    }

    [Theory]
    [InlineData("Sham Realty", "sham-realty")]
    [InlineData("  Sham   Realty  ", "sham-realty")]
    [InlineData("Sham/Realty!!", "sham-realty")]
    [InlineData("--sham--realty--", "sham-realty")]
    public void NormalizeSlug_CollapsesSeparatorsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, Agency.NormalizeSlug(input));
    }

    [Fact]
    public void NormalizeSlug_KeepsArabicLetters()
    {
        // A Syrian office should be able to have an Arabic slug rather than a
        // transliteration nobody would type.
        Assert.Equal("مكتب-الشام", Agency.NormalizeSlug("مكتب الشام"));
    }

    [Fact]
    public void NormalizeSlug_WithNothingUsable_Throws()
    {
        Assert.Throws<DomainException>(() => Agency.NormalizeSlug("!!!"));
    }

    [Fact]
    public void TransferOwnership_ToTheCurrentOwner_Throws()
    {
        var owner = Guid.NewGuid();
        var agency = BuildAgency(owner);

        Assert.Throws<DomainException>(
            () => agency.TransferOwnership(owner, DateTime.UtcNow));
    }

    [Fact]
    public void TransferOwnership_ToAnUnspecifiedUser_Throws()
    {
        var agency = BuildAgency(Guid.NewGuid());

        Assert.Throws<DomainException>(
            () => agency.TransferOwnership(Guid.Empty, DateTime.UtcNow));
    }

    [Fact]
    public void TransferOwnership_ToAnotherUser_MovesOwnerUserId()
    {
        var agency = BuildAgency(Guid.NewGuid());
        var newOwner = Guid.NewGuid();

        agency.TransferOwnership(newOwner, DateTime.UtcNow);

        Assert.Equal(newOwner, agency.OwnerUserId);
    }

    [Fact]
    public void SetLogo_StoresUrlAndPublicId()
    {
        var agency = BuildAgency(Guid.NewGuid());

        agency.SetLogo("https://cdn.example.com/logo.png", "agency-logos/abc123", DateTime.UtcNow);

        Assert.Equal("https://cdn.example.com/logo.png", agency.LogoUrl);
        Assert.Equal("agency-logos/abc123", agency.LogoPublicId);
    }

    [Fact]
    public void SetLogo_WithNulls_ClearsTheLogo()
    {
        var agency = BuildAgency(Guid.NewGuid());
        agency.SetLogo("https://cdn.example.com/logo.png", "agency-logos/abc123", DateTime.UtcNow);

        agency.SetLogo(null, null, DateTime.UtcNow);

        Assert.Null(agency.LogoUrl);
        Assert.Null(agency.LogoPublicId);
    }

    // ── Domain: membership on UserAccount ────────────────────────

    [Fact]
    public void JoinAgency_WhenAlreadyInAnotherAgency_Throws()
    {
        var account = BuildAccount();
        account.JoinAgency(Guid.NewGuid(), DateTime.UtcNow);

        // One membership at a time, or "which office does this agent represent on this
        // listing?" has no single answer.
        Assert.Throws<InvalidOperationException>(
            () => account.JoinAgency(Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void JoinAgency_Twice_IntoTheSameAgency_IsAllowed()
    {
        var account = BuildAccount();
        var agencyId = Guid.NewGuid();

        account.JoinAgency(agencyId, DateTime.UtcNow);
        account.JoinAgency(agencyId, DateTime.UtcNow);

        Assert.Equal(agencyId, account.AgencyId);
    }

    [Fact]
    public void LeaveAgency_ClearsMembershipAndJoinDate()
    {
        var account = BuildAccount();
        account.JoinAgency(Guid.NewGuid(), DateTime.UtcNow);

        account.LeaveAgency(DateTime.UtcNow);

        Assert.Null(account.AgencyId);
        Assert.Null(account.AgencyJoinedAt);
    }

    // ── Create agency ────────────────────────────────────────────

    [Fact]
    public async Task CreateAgency_AttachesOwnerAndGrantsTheOwnerRole()
    {
        var account = BuildAccount();
        var repo = BuildRepository(account);
        var identity = BuildIdentity();
        var handler = CreateHandler(repo, identity);

        var result = await handler.Handle(
            BuildCreateCommand(account.Id, "Sham Realty"),
            CancellationToken.None);

        Assert.Equal("sham-realty", result.Slug);
        Assert.Equal(account.Id, result.OwnerUserId);
        Assert.NotNull(account.AgencyId);

        identity.Verify(
            x => x.AssignRoleAsync(
                account.Id,
                RoleNames.AgencyOwner,
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAgency_WithATakenSlug_IsRejected()
    {
        var account = BuildAccount();
        var repo = BuildRepository(account);
        repo.Setup(x => x.SlugExistsAsync("sham-realty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = CreateHandler(repo, BuildIdentity());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            BuildCreateCommand(account.Id, "Sham Realty"),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAgency_WhenAlreadyAMemberSomewhere_IsRejected()
    {
        var account = BuildAccount();
        account.JoinAgency(Guid.NewGuid(), DateTime.UtcNow);

        var handler = CreateHandler(BuildRepository(account), BuildIdentity());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            BuildCreateCommand(account.Id, "Sham Realty"),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAgency_SucceedsEvenIfTheRoleGrantFails()
    {
        var account = BuildAccount();
        var repo = BuildRepository(account);

        var identity = new Mock<IAdminIdentityService>();
        identity
            .Setup(x => x.AssignRoleAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.UserNotFound());

        var handler = CreateHandler(repo, identity);

        // The agency row is already committed at this point. Failing the request would
        // report "creation failed" for an agency that exists — the recoverable direction is
        // to return it and log the missing role for an administrator to fix.
        var result = await handler.Handle(
            BuildCreateCommand(account.Id, "Sham Realty"),
            CancellationToken.None);

        Assert.Equal("sham-realty", result.Slug);
    }

    // ── Public agency page ──────────────────────────────────────────

    [Fact]
    public async Task GetAgencyBySlug_ShowsOnlyTheOwner_UntilRealConsentExists()
    {
        // Interim mitigation for B-2 (RELEASE-BLOCKERS-AR.md), kept even after the
        // invitation/accept flow landed (see AgencyInvitationTests): this handler has no way
        // to tell "accepted a real invitation" apart from any other route to AgencyId being
        // set, so it still only trusts the owner's own implicit consent for the public page.
        var owner = BuildAccount();
        var agency = BuildAgency(owner.Id);
        var memberOne = BuildAccount();
        memberOne.JoinAgency(agency.Id, DateTime.UtcNow);
        var memberTwo = BuildAccount();
        memberTwo.JoinAgency(agency.Id, DateTime.UtcNow);

        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetBySlugAsync(agency.Slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        repo.Setup(x => x.GetMembersAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([owner, memberOne, memberTwo]);

        var handler = new GetAgencyBySlugQueryHandler(repo.Object);

        var dto = await handler.Handle(
            new GetAgencyBySlugQuery(agency.Slug),
            CancellationToken.None);

        var shown = Assert.Single(dto.Members);
        Assert.Equal(owner.Id, shown.UserId);
        Assert.True(shown.IsOwner);

        // The count still reflects the real roster — only names/photos are withheld, not the
        // fact that the agency has other members.
        Assert.Equal(3, dto.MemberCount);
    }

    [Fact]
    public async Task GetAgencyBySlug_WithAMalformedSlug_IsNotFound_NotBadRequest()
    {
        // RELEASE-BLOCKERS-AR.md B-12: a slug with nothing normalizable (e.g. "---") used to
        // surface Agency.NormalizeSlug's DomainException as-is, which the exception
        // middleware maps to 400 — while a well-formed slug that just does not exist answers
        // 404 from the check below. That difference told an anonymous caller something about
        // slug validity the handler's own doc comment says it must not. Both must answer the
        // same way now.
        var repo = new Mock<IAgencyRepository>();
        var handler = new GetAgencyBySlugQueryHandler(repo.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GetAgencyBySlugQuery("---"),
            CancellationToken.None));

        // The malformed-slug path must never reach the repository — there is nothing to look
        // up once normalization itself failed.
        repo.Verify(
            x => x.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAgencyBySlug_WithAWellFormedButUnknownSlug_IsAlsoNotFound()
    {
        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetBySlugAsync("no-such-office", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);

        var handler = new GetAgencyBySlugQueryHandler(repo.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new GetAgencyBySlugQuery("no-such-office"),
            CancellationToken.None));
    }

    // ── Remove member ────────────────────────────────────────────
    //
    // "Add member" moved to AgencyInvitationTests: B-2 (RELEASE-BLOCKERS-AR.md) replaced
    // AddAgencyMemberCommand's direct attach with CreateAgencyInvitation/Accept/Decline.

    [Fact]
    public async Task RemoveMember_TheOwner_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var ownerAccount = BuildAccount(ownerId);
        ownerAccount.JoinAgency(agency.Id, DateTime.UtcNow);

        var handler = RemoveHandler(BuildRepository(ownerAccount, agency), BuildIdentity());

        // Removing the owner would leave an agency nobody can administer. Deleting the
        // agency is a different, larger action and must not happen as a side effect.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new RemoveAgencyMemberCommand(agency.Id, ownerId, ownerId, null),
            CancellationToken.None));

        Assert.Equal(agency.Id, ownerAccount.AgencyId);
    }

    [Fact]
    public async Task RemoveMember_ByAnUnrelatedUser_IsForbidden()
    {
        var agency = BuildAgency(Guid.NewGuid());
        var member = BuildAccount();
        member.JoinAgency(agency.Id, DateTime.UtcNow);

        var handler = RemoveHandler(BuildRepository(member, agency), BuildIdentity());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new RemoveAgencyMemberCommand(agency.Id, member.Id, Guid.NewGuid(), null),
            CancellationToken.None));

        Assert.Equal(agency.Id, member.AgencyId);
    }

    [Fact]
    public async Task RemoveMember_ByThemselves_IsAllowed()
    {
        var agency = BuildAgency(Guid.NewGuid());
        var member = BuildAccount();
        member.JoinAgency(agency.Id, DateTime.UtcNow);

        var identity = BuildIdentity();
        var handler = RemoveHandler(BuildRepository(member, agency), identity);

        await handler.Handle(
            new RemoveAgencyMemberCommand(agency.Id, member.Id, member.Id, null),
            CancellationToken.None);

        Assert.Null(member.AgencyId);
        identity.Verify(
            x => x.RemoveRoleAsync(
                member.Id, RoleNames.AgencyAgent, member.Id,
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveMember_DoesNotTouchTheirListings()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var member = BuildAccount();
        member.JoinAgency(agency.Id, DateTime.UtcNow);

        var repo = BuildRepository(member, agency);
        var handler = RemoveHandler(repo, BuildIdentity());

        await handler.Handle(
            new RemoveAgencyMemberCommand(agency.Id, member.Id, ownerId, null),
            CancellationToken.None);

        // A member leaving must never unpublish or reassign the listings they own.
        repo.Verify(
            x => x.ClearAgencyAttributionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Application: update profile (RELEASE-BLOCKERS-AR.md B-4) ──

    [Fact]
    public async Task UpdateAgency_ByOwner_ChangesProfileAndReturnsIt()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);
        owner.JoinAgency(agency.Id, DateTime.UtcNow);

        var repo = BuildRepository(owner, agency);
        var handler = UpdateHandler(repo);

        var dto = await handler.Handle(
            new UpdateAgencyCommand(
                AgencyId: agency.Id,
                Name: "مكتب الشام الجديد",
                Description: "وصف جديد",
                ContactEmail: "new@sham-realty.sy",
                ContactPhone: "0999999999",
                City: "دمشق",
                RequestingUserId: ownerId),
            CancellationToken.None);

        Assert.Equal("مكتب الشام الجديد", agency.Name);
        Assert.Equal("وصف جديد", agency.Description);
        Assert.Equal("مكتب الشام الجديد", dto.Name);
    }

    [Fact]
    public async Task UpdateAgency_ByNonOwner_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var handler = UpdateHandler(repo);

        // Holding AgencyOwner elsewhere is not enough — it must be THIS agency's owner.
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new UpdateAgencyCommand(
                agency.Id, "اسم آخر", null, null, null, null,
                RequestingUserId: Guid.NewGuid()),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAgency_WhenAgencyMissing_IsNotFound()
    {
        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);

        var handler = UpdateHandler(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new UpdateAgencyCommand(
                Guid.NewGuid(), "اسم", null, null, null, null, Guid.NewGuid()),
            CancellationToken.None));
    }

    // ── Application: deactivate = "delete" (RELEASE-BLOCKERS-AR.md B-4) ──

    [Fact]
    public async Task DeactivateAgency_ByOwner_TurnsItOff()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var handler = DeactivateHandler(repo);

        await handler.Handle(
            new DeactivateAgencyCommand(agency.Id, ownerId),
            CancellationToken.None);

        Assert.False(agency.IsActive);
    }

    [Fact]
    public async Task DeactivateAgency_ByNonOwner_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var handler = DeactivateHandler(repo);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new DeactivateAgencyCommand(agency.Id, Guid.NewGuid()),
            CancellationToken.None));

        Assert.True(agency.IsActive);
    }

    [Fact]
    public async Task DeactivateAgency_WhenAlreadyInactive_IsIdempotent()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        agency.Deactivate(DateTime.UtcNow);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var handler = DeactivateHandler(repo);

        // DELETE is idempotent — deactivating an already-inactive agency must not throw.
        await handler.Handle(
            new DeactivateAgencyCommand(agency.Id, ownerId),
            CancellationToken.None);

        Assert.False(agency.IsActive);
    }

    [Fact]
    public async Task DeactivateAgency_WhenAgencyMissing_IsNotFound()
    {
        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);

        var handler = DeactivateHandler(repo);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new DeactivateAgencyCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None));
    }

    // ── Application: transfer ownership (B-4b) ────────────────────

    [Fact]
    public async Task TransferOwnership_ByOwner_ToAnExistingMember_Succeeds()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var newOwner = BuildAccount();
        newOwner.JoinAgency(agency.Id, DateTime.UtcNow);

        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        repo.Setup(x => x.GetUserAccountAsync(newOwner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newOwner);
        repo.Setup(x => x.GetMembersAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([newOwner]);

        var identity = BuildIdentity();
        var handler = TransferHandler(repo, identity);

        var dto = await handler.Handle(
            new TransferAgencyOwnershipCommand(agency.Id, newOwner.Id, ownerId, null),
            CancellationToken.None);

        Assert.Equal(newOwner.Id, agency.OwnerUserId);
        Assert.Equal(newOwner.Id, dto.OwnerUserId);

        identity.Verify(
            x => x.RemoveRoleAsync(
                ownerId, RoleNames.AgencyOwner, ownerId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        identity.Verify(
            x => x.AssignRoleAsync(
                ownerId, RoleNames.AgencyAgent, ownerId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        identity.Verify(
            x => x.AssignRoleAsync(
                newOwner.Id, RoleNames.AgencyOwner, ownerId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TransferOwnership_ByNonOwner_IsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);

        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);

        var handler = TransferHandler(repo, BuildIdentity());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new TransferAgencyOwnershipCommand(agency.Id, Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None));

        Assert.Equal(ownerId, agency.OwnerUserId);
    }

    [Fact]
    public async Task TransferOwnership_ToANonMember_IsRejected()
    {
        // The new owner must already have gone through the invitation/accept consent flow
        // (B-2) — a transfer must never be able to hand ownership to someone who never
        // agreed to join the agency at all.
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var outsider = BuildAccount();

        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        repo.Setup(x => x.GetUserAccountAsync(outsider.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outsider);

        var handler = TransferHandler(repo, BuildIdentity());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new TransferAgencyOwnershipCommand(agency.Id, outsider.Id, ownerId, null),
            CancellationToken.None));

        Assert.Equal(ownerId, agency.OwnerUserId);
    }

    [Fact]
    public async Task TransferOwnership_ToTheCurrentOwner_IsRejectedAsConflict()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var ownerAccount = BuildAccount(ownerId);
        ownerAccount.JoinAgency(agency.Id, DateTime.UtcNow);

        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agency);
        repo.Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerAccount);

        var handler = TransferHandler(repo, BuildIdentity());

        // The domain's own DomainException guard is translated to the same ConflictException
        // every other "wrong state" case in this handler uses.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new TransferAgencyOwnershipCommand(agency.Id, ownerId, ownerId, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task TransferOwnership_WhenAgencyMissing_IsNotFound()
    {
        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);

        var handler = TransferHandler(repo, BuildIdentity());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new TransferAgencyOwnershipCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None));
    }

    // ── Application: set logo (B-4b) ───────────────────────────────

    [Fact]
    public async Task SetLogo_ByOwner_UploadsAndReplacesTheOldOne()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        agency.SetLogo("https://cdn.example.com/old.png", "agency-logos/old", DateTime.UtcNow);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var storage = new Mock<IMediaStorageService>();
        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), "agency-logos",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success(
                "https://cdn.example.com/new.png", "agency-logos/new"));

        var handler = SetLogoHandler(repo, storage);

        var result = await handler.Handle(
            new SetAgencyLogoCommand(agency.Id, ownerId, BuildPngFile()),
            CancellationToken.None);

        Assert.Equal(SetAgencyLogoStatus.Success, result.Status);
        Assert.Equal("https://cdn.example.com/new.png", agency.LogoUrl);
        Assert.Equal("agency-logos/new", agency.LogoPublicId);

        // The old logo is deleted only after the new one is safely persisted.
        storage.Verify(
            x => x.DeleteImageAsync("agency-logos/old", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetLogo_ByNonOwner_IsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var handler = SetLogoHandler(repo, new Mock<IMediaStorageService>());

        var result = await handler.Handle(
            new SetAgencyLogoCommand(agency.Id, Guid.NewGuid(), BuildPngFile()),
            CancellationToken.None);

        Assert.Equal(SetAgencyLogoStatus.Forbidden, result.Status);
        Assert.Null(agency.LogoUrl);
    }

    [Fact]
    public async Task SetLogo_WhenAgencyMissing_IsNotFound()
    {
        var repo = new Mock<IAgencyRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);

        var handler = SetLogoHandler(repo, new Mock<IMediaStorageService>());

        var result = await handler.Handle(
            new SetAgencyLogoCommand(Guid.NewGuid(), Guid.NewGuid(), BuildPngFile()),
            CancellationToken.None);

        Assert.Equal(SetAgencyLogoStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task SetLogo_WhenStorageFails_DoesNotChangeTheAgency()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var storage = new Mock<IMediaStorageService>();
        storage
            .Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Failed("storage unavailable"));

        var handler = SetLogoHandler(repo, storage);

        var result = await handler.Handle(
            new SetAgencyLogoCommand(agency.Id, ownerId, BuildPngFile()),
            CancellationToken.None);

        Assert.Equal(SetAgencyLogoStatus.StorageFailed, result.Status);
        Assert.Null(agency.LogoUrl);
    }

    [Fact]
    public async Task SetLogo_WithAnOversizedFile_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agency = BuildAgency(ownerId);
        var owner = BuildAccount(ownerId);

        var repo = BuildRepository(owner, agency);
        var handler = SetLogoHandler(repo, new Mock<IMediaStorageService>());

        var oversized = new SetAgencyLogoFileDto(
            new MemoryStream(new byte[8]), "logo.png", "image/png", 3_000_000);

        var result = await handler.Handle(
            new SetAgencyLogoCommand(agency.Id, ownerId, oversized),
            CancellationToken.None);

        Assert.Equal(SetAgencyLogoStatus.ValidationFailed, result.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Agency BuildAgency(Guid ownerId) => Agency.Create(
        "مكتب الشام العقاري",
        "sham-realty",
        ownerId,
        "SY",
        DateTime.UtcNow);

    private static UserAccount BuildAccount(Guid? id = null) => UserAccount.Create(
        id ?? Guid.NewGuid(),
        "نعيم",
        "بزازة",
        DateTime.UtcNow);

    private static CreateAgencyCommand BuildCreateCommand(Guid userId, string name)
        => new(
            Name: name,
            Slug: name,
            CountryCode: "SY",
            Description: null,
            ContactEmail: null,
            ContactPhone: null,
            City: null,
            LicenseNumber: null,
            RequestingUserId: userId,
            IpAddress: null);

    private static Mock<IAgencyRepository> BuildRepository(
        UserAccount account,
        Agency? agency = null)
    {
        var repo = new Mock<IAgencyRepository>();

        repo.Setup(x => x.GetUserAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        repo.Setup(x => x.SlugExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(x => x.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agency?)null);
        repo.Setup(x => x.CountMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(x => x.GetMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([account]);

        if (agency is not null)
        {
            repo.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(agency);
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

        identity
            .Setup(x => x.RemoveRoleAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminOperationResult.Ok("removed"));

        return identity;
    }

    private static CreateAgencyCommandHandler CreateHandler(
        Mock<IAgencyRepository> repo,
        Mock<IAdminIdentityService> identity)
        => new(
            repo.Object,
            identity.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<CreateAgencyCommandHandler>.Instance);

    private static RemoveAgencyMemberCommandHandler RemoveHandler(
        Mock<IAgencyRepository> repo,
        Mock<IAdminIdentityService> identity)
        => new(
            repo.Object,
            identity.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<RemoveAgencyMemberCommandHandler>.Instance);

    private static UpdateAgencyCommandHandler UpdateHandler(Mock<IAgencyRepository> repo)
        => new(repo.Object, Mock.Of<IUnitOfWork>());

    private static DeactivateAgencyCommandHandler DeactivateHandler(Mock<IAgencyRepository> repo)
        => new(
            repo.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<DeactivateAgencyCommandHandler>.Instance);

    private static TransferAgencyOwnershipCommandHandler TransferHandler(
        Mock<IAgencyRepository> repo,
        Mock<IAdminIdentityService> identity)
        => new(
            repo.Object,
            identity.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<TransferAgencyOwnershipCommandHandler>.Instance);

    private static SetAgencyLogoCommandHandler SetLogoHandler(
        Mock<IAgencyRepository> repo,
        Mock<IMediaStorageService> storage)
        => new(
            repo.Object,
            storage.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<SetAgencyLogoCommandHandler>.Instance);

    /// <summary>A minimal-but-valid PNG signature, so the handler's own file-content check passes.</summary>
    private static SetAgencyLogoFileDto BuildPngFile()
    {
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return new SetAgencyLogoFileDto(
            new MemoryStream(pngHeader), "logo.png", "image/png", pngHeader.Length);
    }
}
