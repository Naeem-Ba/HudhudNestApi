using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Application.Valuation.Queries.GetComparableListings;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.Commands.CreateValuationInquiry;

/// <summary>
/// Stage 7 — the orchestration this module has been missing since Stage 2: the actual
/// "customer submits a valuation request" entry point that creates and persists a
/// ValuationInquiry, runs the Fast Path (Stage 3) against it, and — only when the Fast Path
/// alone did not already answer the request in full — runs office matching (Stage 4) and
/// persists the resulting invitations (Stage 5's own repository surface).
///
/// This was flagged as an explicit, deliberately-deferred gap in every completion report from
/// Stage 4 onward ("no command/controller yet creates and persists a ValuationInquiry from a
/// live request"). Stage 7 (Customer UI) is where that stops being deferrable: a customer-
/// facing UI has nothing to submit to without it.
///
/// RequesterId is nullable and resolved by the caller (the Controller) from the auth token —
/// null for an anonymous visitor, exactly like ValuationInquiry.RequesterId's own design.
/// </summary>
public sealed record CreateValuationInquiryCommand(
    Guid? RequesterId,
    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? Area,
    int? Rooms,
    ListingType RequestType) : IRequest<CreateValuationInquiryResultDto>;

public sealed class CreateValuationInquiryCommandValidator : AbstractValidator<CreateValuationInquiryCommand>
{
    public CreateValuationInquiryCommandValidator()
    {
        // Field-presence/shape checks belong here; the finer domain invariants (child-requires-
        // parent for District/Neighborhood, positive Area/Rooms, etc.) are already enforced by
        // ValuationInquiry.Create itself and are not duplicated here.
        RuleFor(x => x.GovernorateId)
            .GreaterThan(0).WithMessage("لا يمكن إنشاء طلب تقييم بلا محافظة.");

        RuleFor(x => x.RequestType)
            .IsInEnum().WithMessage("نوع الطلب غير معروف.");
    }
}

public sealed class CreateValuationInquiryCommandHandler
    : IRequestHandler<CreateValuationInquiryCommand, CreateValuationInquiryResultDto>
{
    private readonly IValuationInquiryRepository _inquiries;
    private readonly IValuationOfficeInvitationRepository _invitations;
    private readonly IOfficeMatchingService _officeMatching;
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateValuationInquiryCommandHandler> _logger;

    public CreateValuationInquiryCommandHandler(
        IValuationInquiryRepository inquiries,
        IValuationOfficeInvitationRepository invitations,
        IOfficeMatchingService officeMatching,
        IMediator mediator,
        IUnitOfWork unitOfWork,
        ILogger<CreateValuationInquiryCommandHandler> logger)
    {
        _inquiries = inquiries;
        _invitations = invitations;
        _officeMatching = officeMatching;
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CreateValuationInquiryResultDto> Handle(
        CreateValuationInquiryCommand request,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var inquiry = ValuationInquiry.Create(
            governorateId: request.GovernorateId,
            requestType: request.RequestType,
            utcNow: now,
            requesterId: request.RequesterId,
            propertyTypeId: request.PropertyTypeId,
            area: request.Area,
            rooms: request.Rooms,
            districtId: request.DistrictId,
            neighborhoodId: request.NeighborhoodId);

        await _inquiries.AddAsync(inquiry, ct);

        // Reuses GetComparableListingsQueryHandler as-is (Stage 3) via a nested mediator
        // dispatch rather than re-implementing its logic here — the one deliberate exception
        // to this codebase's "handlers never call other handlers" convention, chosen because
        // duplicating Stage 3's business rules (area tolerance, minimum-3 threshold, messages)
        // in a second place is a worse outcome than one clean, explicit exception to that norm.
        var fastPath = await _mediator.Send(
            new GetComparableListingsQuery(
                InquiryId: inquiry.Id,
                PropertyTypeId: inquiry.PropertyTypeId,
                Area: inquiry.Area,
                GovernorateId: inquiry.GovernorateId,
                DistrictId: inquiry.DistrictId,
                NeighborhoodId: inquiry.NeighborhoodId,
                ListingType: inquiry.RequestType),
            ct);

        var result = new CreateValuationInquiryResultDto
        {
            InquiryId = inquiry.Id,
            ExpiresAt = inquiry.ExpiresAt,
            FastPath = fastPath,
            RequiresOfficeValuation = fastPath.RequiresOfficeValuation,
        };

        if (fastPath.HasComparableListings)
        {
            inquiry.MarkMatchedFromListings(now);
        }

        if (!fastPath.RequiresOfficeValuation)
        {
            // 3+ comparables already answered the request in full. Nothing else will ever
            // move this inquiry forward, so it must finish here rather than sit at
            // MatchedFromListings for up to 24h only to be swept and "expired" by Stage 5 for
            // a request that was, in truth, already fully answered.
            inquiry.MarkCompleted(now);
        }
        else
        {
            inquiry.MarkAwaitingOfficeResponses(now);

            var officeMatch = await _officeMatching.MatchOfficesAsync(
                inquiry.Id, inquiry.GovernorateId, inquiry.DistrictId, inquiry.NeighborhoodId, now, ct);

            if (officeMatch.SelectedOffices.Count > 0)
            {
                await _invitations.AddRangeAsync(
                    officeMatch.SelectedOffices.Select(o => o.Invitation), ct);
            }

            result.OfficeMatchCount = officeMatch.Count;
            result.HasMinimumOfficeCoverage = officeMatch.HasMinimumCoverage;
            result.InsufficientOfficeCoverage = officeMatch.InsufficientOfficeCoverage;

            // Nearby-Governorate disclosure rule.
            result.MatchedNeighboringGovernorate = officeMatch.SelectedOffices
                .Any(o => o.MatchLevel == ValuationMatchLevel.GovernorateNeighboring);
        }

        result.Status = inquiry.Status;

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Valuation inquiry created. InquiryId={InquiryId}, Status={Status}, RequiresOfficeValuation={RequiresOfficeValuation}, OfficeMatchCount={OfficeMatchCount}",
            inquiry.Id,
            inquiry.Status,
            fastPath.RequiresOfficeValuation,
            result.OfficeMatchCount);

        return result;
    }
}
