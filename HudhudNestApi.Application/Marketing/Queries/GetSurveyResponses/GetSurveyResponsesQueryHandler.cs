using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Interfaces;

namespace HudhudNestApi.Application.Marketing.Queries.GetSurveyResponses;

public sealed class GetSurveyResponsesQueryHandler
    : IRequestHandler<GetSurveyResponsesQuery, SurveyResponsesPageDto>
{
    private readonly ISurveyResponseRepository _surveys;

    public GetSurveyResponsesQueryHandler(ISurveyResponseRepository surveys)
        => _surveys = surveys;

    public Task<SurveyResponsesPageDto> Handle(
        GetSurveyResponsesQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        return _surveys.GetPageAsync(page, pageSize, cancellationToken);
    }
}
