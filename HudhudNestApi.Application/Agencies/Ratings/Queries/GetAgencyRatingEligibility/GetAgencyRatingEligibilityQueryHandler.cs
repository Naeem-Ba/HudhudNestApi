using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Ratings.DTOs;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Users.Interfaces;

namespace HudhudNestApi.Application.Agencies.Ratings.Queries.GetAgencyRatingEligibility;

public sealed class GetAgencyRatingEligibilityQueryHandler
    : IRequestHandler<GetAgencyRatingEligibilityQuery, AgencyRatingEligibilityDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyRatingRepository _ratings;
    private readonly IUserAccountRepository _accounts;

    public GetAgencyRatingEligibilityQueryHandler(
        IAgencyRepository agencies,
        IAgencyRatingRepository ratings,
        IUserAccountRepository accounts)
    {
        _agencies = agencies;
        _ratings = ratings;
        _accounts = accounts;
    }

    public async Task<AgencyRatingEligibilityDto> Handle(GetAgencyRatingEligibilityQuery request, CancellationToken ct)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, ct);
        if (agency is null || !agency.IsActive)
            throw new NotFoundException("المكتب العقاري غير موجود.");

        var rater = await _accounts.GetByIdAsync(request.RaterId, ct)
            ?? throw new NotFoundException("الحساب غير موجود.");

        var isMemberOrOwner = AgencyRatingPolicy.IsMemberOrOwner(agency, rater);
        var existing = await _ratings.GetByRaterAndAgencyAsync(agency.Id, rater.Id, ct);

        // Members never need the interaction lookup: they cannot rate whatever it says.
        var hasInteraction = !isMemberOrOwner && await _ratings.HasInteractionAsync(agency.Id, rater.Id, ct);

        return new AgencyRatingEligibilityDto(
            CanRate: !isMemberOrOwner && hasInteraction,
            IsMemberOrOwner: isMemberOrOwner,
            HasInteraction: hasInteraction,
            AlreadyRated: existing is not null,
            ExistingRating: existing is null ? null : AgencyRatingMapper.ToDto(existing, rater));
    }
}
