using MediatR;
using HudhudNestApi.Application.Marketing.DTOs;

namespace HudhudNestApi.Application.Marketing.Queries.GetSurveyStats;

public sealed record GetSurveyStatsQuery : IRequest<SurveyStatsDto>;
