using MediatR;
using PropertyApi.Application.Marketing.DTOs;

namespace PropertyApi.Application.Marketing.Queries.GetSurveyResponses;

public sealed record GetSurveyResponsesQuery(int Page, int PageSize) : IRequest<SurveyResponsesPageDto>;
