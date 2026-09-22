using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;

namespace HudhudNestApi.Application.Marketing.Queries.GetSurveyResponses;

public sealed record GetSurveyResponsesQuery(int Page, int PageSize) : IRequest<SurveyResponsesPageDto>;
