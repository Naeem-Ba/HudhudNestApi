using MediatR;
using HudhudNestApi.Application.Bookings.DTOs;

namespace HudhudNestApi.Application.Bookings.Queries.GetPropertyVisits;

/// <summary>Used by the property owner to view all visit requests.</summary>
public sealed record GetPropertyVisitsQuery(
    Guid PropertyId,
    Guid OwnerId
) : IRequest<IReadOnlyList<VisitDto>>;
