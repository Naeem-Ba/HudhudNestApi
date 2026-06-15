using MediatR;
using PropertyApi.Application.Bookings.DTOs;
using PropertyApi.Domain.Bookings.Enums;

namespace PropertyApi.Application.Bookings.Queries.GetMyVisits;

public sealed record GetMyVisitsQuery(
    Guid        UserId,
    VisitStatus? StatusFilter = null
) : IRequest<IReadOnlyList<VisitDto>>;
