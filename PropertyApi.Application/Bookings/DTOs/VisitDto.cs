using PropertyApi.Domain.Bookings.Enums;

namespace PropertyApi.Application.Bookings.DTOs;

public sealed record VisitDto(
    Guid       Id,
    Guid       PropertyId,
    string     PropertyTitle,
    string     PropertyCity,
    string?    PropertyMainImageUrl,
    Guid       RequesterId,
    string     RequesterName,
    string     VisitorName,
    string     VisitorPhone,
    string?    VisitorNote,
    DateTime   ProposedAt,
    string?    OwnerNote,
    DateTime?  RespondedAt,
    VisitStatus Status,
    DateTime   CreatedAt
);
