using MediatR;
using PropertyApi.Application.Marketing.DTOs;

namespace PropertyApi.Application.Marketing.Queries.GetSurveyStats;

public sealed record GetSurveyStatsQuery : IRequest<SurveyStatsDto>;
