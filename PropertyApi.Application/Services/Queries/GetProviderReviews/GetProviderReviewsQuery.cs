using MediatR;
using PropertyApi.Application.Services.DTOs;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Application.Services.Mapping;
using PropertyApi.Application.Users.Interfaces;

namespace PropertyApi.Application.Services.Queries.GetProviderReviews;

/// <summary>Public — a provider's reviews are shown on their profile page, same visibility
/// as PropertyReview on a property's page.</summary>
public sealed record GetProviderReviewsQuery(
    Guid ServiceProviderId,
    int Page = 1,
    int PageSize = 10) : IRequest<IReadOnlyList<ServiceReviewDto>>;

public sealed class GetProviderReviewsQueryHandler
    : IRequestHandler<GetProviderReviewsQuery, IReadOnlyList<ServiceReviewDto>>
{
    private readonly IServiceReviewRepository _reviews;
    private readonly IUserAccountRepository _users;

    public GetProviderReviewsQueryHandler(IServiceReviewRepository reviews, IUserAccountRepository users)
    {
        _reviews = reviews;
        _users = users;
    }

    public async Task<IReadOnlyList<ServiceReviewDto>> Handle(GetProviderReviewsQuery request, CancellationToken ct)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var reviews = await _reviews.GetByProviderIdAsync(request.ServiceProviderId, page, pageSize, ct);

        var dtos = new List<ServiceReviewDto>(reviews.Count);
        foreach (var review in reviews)
        {
            var reviewer = await _users.GetByIdAsync(review.ReviewerId, ct);
            var reviewerName = reviewer is null
                ? "—"
                : string.IsNullOrWhiteSpace(reviewer.DisplayName)
                    ? $"{reviewer.FirstName} {reviewer.LastName}".Trim()
                    : reviewer.DisplayName!;

            dtos.Add(ServiceMapper.ToDto(review, reviewerName));
        }

        return dtos.AsReadOnly();
    }
}
