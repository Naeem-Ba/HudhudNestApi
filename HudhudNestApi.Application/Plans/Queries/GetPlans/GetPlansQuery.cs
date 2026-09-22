using MediatR;
using HudhudNestApi.Application.Plans.DTOs;

namespace HudhudNestApi.Application.Plans.Queries.GetPlans;

public sealed record GetPlansQuery : IRequest<IReadOnlyList<PlanDto>>;
