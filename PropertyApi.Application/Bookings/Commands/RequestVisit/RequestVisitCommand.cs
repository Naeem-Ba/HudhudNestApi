using MediatR;
using PropertyApi.Application.Bookings.DTOs;

namespace PropertyApi.Application.Bookings.Commands.RequestVisit;

public sealed record RequestVisitCommand(
    Guid PropertyId,
    Guid RequesterId,
    DateTime ProposedAt,
    string VisitorName,
    string VisitorPhone,
    string? VisitorNote) : IRequest<VisitDto>;
