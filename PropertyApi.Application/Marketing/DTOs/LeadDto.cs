namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>Admin-only projection — never returned to the anonymous submitter.</summary>
public sealed record LeadDto(
    Guid Id,
    string FullName,
    string Phone,
    string City,
    string UserType,
    string? Notes,
    string Source,
    string? Campaign,
    Guid? OfferId,
    string? OfferName,
    string Status,
    DateTime CreatedAt);
