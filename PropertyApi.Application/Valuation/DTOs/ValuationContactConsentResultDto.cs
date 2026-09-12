namespace PropertyApi.Application.Valuation.DTOs;

/// <summary>Stage 9 — result of SubmitValuationContactConsentCommand.</summary>
public sealed record ValuationContactConsentResultDto(
    Guid InvitationId,
    DateTime ConsentedAt);
