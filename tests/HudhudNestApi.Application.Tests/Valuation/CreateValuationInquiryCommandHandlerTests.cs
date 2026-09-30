using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Valuation.Commands.CreateValuationInquiry;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Application.Valuation.Queries.GetComparableListings;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Tests.Valuation;

/// <summary>
/// Stage 7 — CreateValuationInquiryCommandHandler's orchestration: which of Stage 3's Fast
/// Path / Stage 4's office matching run, and how the inquiry's own status ends up reflecting
/// that ("never left indefinitely pending for something already fully answered" — the same
/// discipline Stage 5's SLA sweep exists to enforce for everything else). Uses Moq for every
/// dependency, MockBehavior.Strict, same convention this module's other handler tests use.
/// </summary>
public sealed class CreateValuationInquiryCommandHandlerTests
{
    [Fact]
    public async Task Handle_FastPathFindsThreeOrMoreComparables_CompletesImmediately_NeverRunsOfficeMatching()
    {
        var (handler, inquiries, invitations, officeMatching, mediator, unitOfWork) = Build();

        var fastPath = new ComparableListingsResult
        {
            HasComparableListings = true,
            ComparableCount = 5,
            RequiresOfficeValuation = false,
            MatchLevel = ValuationMatchLevel.Neighborhood,
        };

        SetupMediator(mediator, fastPath);
        inquiries.Setup(x => x.AddAsync(It.IsAny<ValuationInquiry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var command = new CreateValuationInquiryCommand(
            RequesterId: Guid.NewGuid(), GovernorateId: 1, DistrictId: 10, NeighborhoodId: 100,
            PropertyTypeId: 2, Area: 120m, Rooms: 3, RequestType: ListingType.ForSale);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(ValuationInquiryStatus.Completed, result.Status);
        Assert.False(result.RequiresOfficeValuation);
        Assert.Equal(0, result.OfficeMatchCount);
        Assert.Same(fastPath, result.FastPath);

        // Office matching must never even be queried once the Fast Path alone answered the
        // request in full.
        officeMatching.Verify(
            x => x.MatchOfficesAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        invitations.Verify(
            x => x.AddRangeAsync(It.IsAny<IEnumerable<ValuationOfficeInvitation>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_FastPathFindsTooFew_RunsOfficeMatching_AndPersistsSelectedInvitations()
    {
        var (handler, inquiries, invitations, officeMatching, mediator, unitOfWork) = Build();

        var fastPath = new ComparableListingsResult
        {
            HasComparableListings = true, // 1-2 comparables: a limited-data preliminary estimate
            ComparableCount = 1,
            RequiresOfficeValuation = true,
            MatchLevel = ValuationMatchLevel.Neighborhood,
        };

        SetupMediator(mediator, fastPath);
        inquiries.Setup(x => x.AddAsync(It.IsAny<ValuationInquiry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var selectedAgencyId = Guid.NewGuid();
        var officeResult = new OfficeMatchingResult
        {
            InquiryId = Guid.NewGuid(), // overwritten by the handler's own inquiry.Id, unused by the assertions below
            SelectedOffices =
            [
                new OfficeMatch
                {
                    AgencyId = selectedAgencyId,
                    MatchLevel = ValuationMatchLevel.District,
                    Invitation = ValuationOfficeInvitation.Create(selectedAgencyId, Guid.NewGuid(), ValuationMatchLevel.District, DateTime.UtcNow),
                },
            ],
            HasMinimumCoverage = false,
            InsufficientOfficeCoverage = true,
            MatchingCompleted = true,
        };

        officeMatching
            .Setup(x => x.MatchOfficesAsync(It.IsAny<Guid>(), 1, 10, 100, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(officeResult);
        invitations
            .Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<ValuationOfficeInvitation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new CreateValuationInquiryCommand(
            RequesterId: null, GovernorateId: 1, DistrictId: 10, NeighborhoodId: 100,
            PropertyTypeId: null, Area: null, Rooms: null, RequestType: ListingType.ForRent);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(ValuationInquiryStatus.AwaitingOfficeResponses, result.Status);
        Assert.True(result.RequiresOfficeValuation);
        Assert.Equal(1, result.OfficeMatchCount);
        Assert.False(result.HasMinimumOfficeCoverage);
        Assert.True(result.InsufficientOfficeCoverage);
        Assert.False(result.MatchedNeighboringGovernorate);

        invitations.Verify(
            x => x.AddRangeAsync(
                It.Is<IEnumerable<ValuationOfficeInvitation>>(list => list.Single().AgencyId == selectedAgencyId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoComparablesAtAll_SkipsMatchedFromListings_GoesStraightToAwaitingOfficeResponses()
    {
        var (handler, inquiries, invitations, officeMatching, mediator, unitOfWork) = Build();

        var fastPath = new ComparableListingsResult
        {
            HasComparableListings = false,
            ComparableCount = 0,
            RequiresOfficeValuation = true,
            MatchLevel = ValuationMatchLevel.Governorate,
        };

        SetupMediator(mediator, fastPath);
        inquiries.Setup(x => x.AddAsync(It.IsAny<ValuationInquiry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        officeMatching
            .Setup(x => x.MatchOfficesAsync(It.IsAny<Guid>(), 1, null, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OfficeMatchingResult
            {
                InquiryId = Guid.NewGuid(),
                SelectedOffices = [],
                HasMinimumCoverage = false,
                InsufficientOfficeCoverage = true,
                MatchingCompleted = true,
            });

        var command = new CreateValuationInquiryCommand(
            RequesterId: null, GovernorateId: 1, DistrictId: null, NeighborhoodId: null,
            PropertyTypeId: null, Area: null, Rooms: null, RequestType: ListingType.ForSale);

        var result = await handler.Handle(command, CancellationToken.None);

        // Pending -> AwaitingOfficeResponses directly (no MarkMatchedFromListings call, since
        // there was nothing to mark) is itself a valid domain transition -- if it were not,
        // this would already have thrown DomainException instead of returning.
        Assert.Equal(ValuationInquiryStatus.AwaitingOfficeResponses, result.Status);
        Assert.Equal(0, result.OfficeMatchCount);
        // No AddRangeAsync setup exists on the Strict `invitations` mock for an empty
        // selection -- reaching it would already throw.
    }

    [Fact]
    public async Task Handle_OfficeMatchIncludesNeighboringGovernorate_SetsDisclosureFlag()
    {
        var (handler, inquiries, invitations, officeMatching, mediator, unitOfWork) = Build();

        var fastPath = new ComparableListingsResult { HasComparableListings = false, RequiresOfficeValuation = true };
        SetupMediator(mediator, fastPath);
        inquiries.Setup(x => x.AddAsync(It.IsAny<ValuationInquiry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var agencyId = Guid.NewGuid();
        officeMatching
            .Setup(x => x.MatchOfficesAsync(It.IsAny<Guid>(), 1, null, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OfficeMatchingResult
            {
                InquiryId = Guid.NewGuid(),
                SelectedOffices =
                [
                    new OfficeMatch
                    {
                        AgencyId = agencyId,
                        MatchLevel = ValuationMatchLevel.GovernorateNeighboring,
                        Invitation = ValuationOfficeInvitation.Create(agencyId, Guid.NewGuid(), ValuationMatchLevel.GovernorateNeighboring, DateTime.UtcNow),
                    },
                ],
                HasMinimumCoverage = false,
                InsufficientOfficeCoverage = true,
                MatchingCompleted = true,
            });
        invitations.Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<ValuationOfficeInvitation>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var command = new CreateValuationInquiryCommand(
            RequesterId: null, GovernorateId: 1, DistrictId: null, NeighborhoodId: null,
            PropertyTypeId: null, Area: null, Rooms: null, RequestType: ListingType.ForSale);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.MatchedNeighboringGovernorate);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static void SetupMediator(Mock<IMediator> mediator, ComparableListingsResult fastPath)
        => mediator
            .Setup(x => x.Send(It.IsAny<GetComparableListingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fastPath);

    private static (
        CreateValuationInquiryCommandHandler Handler,
        Mock<IValuationInquiryRepository> Inquiries,
        Mock<IValuationOfficeInvitationRepository> Invitations,
        Mock<IOfficeMatchingService> OfficeMatching,
        Mock<IMediator> Mediator,
        Mock<IUnitOfWork> UnitOfWork) Build()
    {
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var officeMatching = new Mock<IOfficeMatchingService>(MockBehavior.Strict);
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var handler = new CreateValuationInquiryCommandHandler(
            inquiries.Object,
            invitations.Object,
            officeMatching.Object,
            mediator.Object,
            unitOfWork.Object,
            TimeProvider.System,
            NullLogger<CreateValuationInquiryCommandHandler>.Instance);

        return (handler, inquiries, invitations, officeMatching, mediator, unitOfWork);
    }
}
