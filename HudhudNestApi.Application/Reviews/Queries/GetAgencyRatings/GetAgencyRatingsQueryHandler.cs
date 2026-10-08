using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Reviews.DTOs;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Reviews.Mapping;

namespace HudhudNestApi.Application.Reviews.Queries.GetAgencyRatings;

public sealed class GetAgencyRatingsQueryHandler : IRequestHandler<GetAgencyRatingsQuery, AgencyRatingSummaryDto>
{
    private readonly IAgencyRatingRepository _ratings;
    private readonly IAgencyRepository _agencies;

    public GetAgencyRatingsQueryHandler(IAgencyRatingRepository ratings, IAgencyRepository agencies)
    {
        _ratings = ratings;
        _agencies = agencies;
    }

    public async Task<AgencyRatingSummaryDto> Handle(GetAgencyRatingsQuery request, CancellationToken ct)
    {
        // Same visibility as the public agency page: an inactive or deleted agency is not public.
        var agency = await _agencies.GetByIdAsync(request.AgencyId, ct);
        if (agency is not { IsDeleted: false, IsActive: true })
            throw new NotFoundException("المكتب غير موجود.");

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var averages = await _ratings.GetAveragesAsync(agency.Id, ct);
        var (ratings, totalCount) = await _ratings.GetByAgencyAsync(agency.Id, page, pageSize, ct);

        return new AgencyRatingSummaryDto(
            AverageService: Math.Round(averages.Service, 1),
            AverageProfessionalism: Math.Round(averages.Professionalism, 1),
            AverageReliability: Math.Round(averages.Reliability, 1),
            AverageOverallExperience: Math.Round(averages.OverallExperience, 1),
            AverageOverall: Math.Round(averages.Overall, 1),
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            Ratings: ratings.Select(AgencyRatingMapper.ToDto).ToList().AsReadOnly());
    }
}
