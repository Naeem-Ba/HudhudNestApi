using MediatR;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Interfaces;

namespace PropertyApi.Application.Marketing.Queries.GetSurveyStats;

public sealed class GetSurveyStatsQueryHandler : IRequestHandler<GetSurveyStatsQuery, SurveyStatsDto>
{
    private readonly ISurveyResponseRepository _surveys;

    public GetSurveyStatsQueryHandler(ISurveyResponseRepository surveys)
        => _surveys = surveys;

    public Task<SurveyStatsDto> Handle(GetSurveyStatsQuery request, CancellationToken cancellationToken)
        => _surveys.GetStatsAsync(cancellationToken);
}
