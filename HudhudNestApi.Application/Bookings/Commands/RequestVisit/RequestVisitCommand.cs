using MediatR;
using HudhudNestApi.Application.Bookings.DTOs;

namespace HudhudNestApi.Application.Bookings.Commands.RequestVisit;

public sealed record RequestVisitCommand(
    Guid PropertyId,
    Guid RequesterId,
    DateTime ProposedAt,
    string VisitorName,
    string VisitorPhone,
    string? VisitorNote) : IRequest<VisitDto>;

