using MediatR;
using HudhudNestApi.Application.Bookings.DTOs;
using HudhudNestApi.Domain.Bookings.Enums;

namespace HudhudNestApi.Application.Bookings.Queries.GetMyVisits;

public sealed record GetMyVisitsQuery(
    Guid UserId,
    VisitStatus? StatusFilter = null
) : IRequest<IReadOnlyList<VisitDto>>;
