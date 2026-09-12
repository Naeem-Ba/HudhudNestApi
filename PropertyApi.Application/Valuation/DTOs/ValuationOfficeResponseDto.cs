namespace PropertyApi.Application.Valuation.DTOs;

/// <summary>Returned to the office after SubmitOfficeResponseCommand succeeds.</summary>
public sealed record ValuationOfficeResponseDto(
    Guid Id,
    Guid InvitationId,
    decimal EstimatedPrice,
    string? Notes,
    DateTime SubmittedAt);
