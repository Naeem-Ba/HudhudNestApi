using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Ratings.DTOs;
using HudhudNestApi.Application.Common.Exceptions;

namespace HudhudNestApi.Application.Agencies.Ratings.Queries.GetAgencyRatings;

public sealed class GetAgencyRatingsQueryHandler : IRequestHandler<GetAgencyRatingsQuery, AgencyRatingSummaryDto>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyRatingRepository _ratings;

    public GetAgencyRatingsQueryHandler(IAgencyRepository agencies, IAgencyRatingRepository ratings)
    {
        _agencies = agencies;
        _ratings = ratings;
    }

    public async Task<AgencyRatingSummaryDto> Handle(GetAgencyRatingsQuery request, CancellationToken ct)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, ct);
        if (agency is null || !agency.IsActive)
            throw new NotFoundException("المكتب العقاري غير موجود.");

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var averages = await _ratings.GetAveragesAsync(agency.Id, ct);
        var (rows, total) = await _ratings.GetPageAsync(agency.Id, page, pageSize, ct);

        var items = rows.Select(r => AgencyRatingMapper.ToDto(r, r.Rater)).ToList().AsReadOnly();

        return new AgencyRatingSummaryDto(
            AverageService: Math.Round(averages.Service, 1),
            AverageProfessionalism: Math.Round(averages.Professionalism, 1),
            AverageReliability: Math.Round(averages.Reliability, 1),
            AverageOverallExperience: Math.Round(averages.OverallExperience, 1),
            AverageOverall: Math.Round(averages.Overall, 1),
            TotalCount: total,
            Page: page,
            PageSize: pageSize,
            Ratings: items);
    }
}
