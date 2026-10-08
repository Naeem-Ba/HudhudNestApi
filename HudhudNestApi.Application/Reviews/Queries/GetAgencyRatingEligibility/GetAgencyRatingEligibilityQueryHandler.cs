using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Reviews.DTOs;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Reviews.Mapping;

namespace HudhudNestApi.Application.Reviews.Queries.GetAgencyRatingEligibility;

public sealed class GetAgencyRatingEligibilityQueryHandler
    : IRequestHandler<GetAgencyRatingEligibilityQuery, AgencyRatingEligibilityDto>
{
    private readonly IAgencyRatingRepository _ratings;
    private readonly IAgencyRepository _agencies;

    public GetAgencyRatingEligibilityQueryHandler(IAgencyRatingRepository ratings, IAgencyRepository agencies)
    {
        _ratings = ratings;
        _agencies = agencies;
    }

    public async Task<AgencyRatingEligibilityDto> Handle(GetAgencyRatingEligibilityQuery request, CancellationToken ct)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, ct);
        if (agency is not { IsDeleted: false, IsActive: true })
            throw new NotFoundException("المكتب غير موجود.");

        var rater = await _agencies.GetUserAccountAsync(request.RaterId, ct);
        var isMemberOrOwner = agency.OwnerUserId == request.RaterId || rater?.AgencyId == agency.Id;

        var existing = await _ratings.GetByRaterAndAgencyAsync(agency.Id, request.RaterId, ct);
        var hasInteraction = !isMemberOrOwner &&
            await _ratings.HasInteractionWithAgencyAsync(request.RaterId, agency.Id, ct);

        return new AgencyRatingEligibilityDto(
            CanRate: !isMemberOrOwner && hasInteraction,
            IsMemberOrOwner: isMemberOrOwner,
            HasInteraction: hasInteraction,
            AlreadyRated: existing is not null,
            ExistingRating: existing is null ? null : AgencyRatingMapper.ToDto(existing));
    }
}
