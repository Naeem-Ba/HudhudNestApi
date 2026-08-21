using MediatR;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Reviews.DTOs;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Application.Users.Messaging.Interfaces;

namespace PropertyApi.Application.Reviews.Queries.GetRatingEligibility;

public sealed class GetRatingEligibilityQueryHandler
    : IRequestHandler<GetRatingEligibilityQuery, RatingEligibilityDto>
{
    private readonly IUserRatingRepository _ratings;
    private readonly IVisitRepository _visits;
    private readonly IMessageRepository _messages;

    public GetRatingEligibilityQueryHandler(
        IUserRatingRepository ratings,
        IVisitRepository visits,
        IMessageRepository messages)
    {
        _ratings = ratings;
        _visits = visits;
        _messages = messages;
    }

    public async Task<RatingEligibilityDto> Handle(
        GetRatingEligibilityQuery request,
        CancellationToken ct)
    {
        if (request.RatedUserId == request.RaterId)
        {
            return new RatingEligibilityDto(
                CanRate: false,
                IsSelf: true,
                AlreadyRated: false,
                ExistingRating: null);
        }

        var existing = await _ratings.GetByRaterAndRatedAsync(
            request.RatedUserId, request.RaterId, ct);

        var hasCompletedVisit = await _visits.HasCompletedVisitWithOwnerAsync(
            request.RaterId, request.RatedUserId, ct);

        var canRate = hasCompletedVisit ||
            await _messages.HasAnyConversationAsync(request.RaterId, request.RatedUserId, ct);

        UserRatingDto? existingDto = existing is null
            ? null
            : new UserRatingDto(
                Id: existing.Id,
                RatedUserId: existing.RatedUserId,
                RaterId: existing.RaterId,
                RaterName: string.Empty, // غير مطلوب هنا — الواجهة تعرف اسم المُقيِّم (المستخدم الحالي) أصلًا
                RaterImageUrl: null,
                Credibility: existing.Credibility,
                Safety: existing.Safety,
                ResponseSpeed: existing.ResponseSpeed,
                Transparency: existing.Transparency,
                OverallScore: existing.OverallScore,
                Comment: existing.Comment,
                CreatedAt: existing.CreatedAt);

        return new RatingEligibilityDto(
            CanRate: canRate,
            IsSelf: false,
            AlreadyRated: existing is not null,
            ExistingRating: existingDto);
    }
}
