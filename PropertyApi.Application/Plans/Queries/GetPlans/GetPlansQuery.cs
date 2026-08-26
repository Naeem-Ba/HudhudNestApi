using MediatR;
using PropertyApi.Application.Plans.DTOs;

namespace PropertyApi.Application.Plans.Queries.GetPlans;

public sealed record GetPlansQuery : IRequest<IReadOnlyList<PlanDto>>;
