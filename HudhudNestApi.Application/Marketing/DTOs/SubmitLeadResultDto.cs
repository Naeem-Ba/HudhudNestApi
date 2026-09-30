namespace HudhudNestApi.Application.Marketing.DTOs;

/// <summary>
/// The lead is always saved regardless of the offer outcome — a submission is never
/// rejected just because the offer the visitor tried to claim ran out between page load
/// and submit. <see cref="OfferApplied"/> tells the frontend whether the claim actually
/// went through, so it can show an honest message instead of assuming success.
/// </summary>
public sealed record SubmitLeadResultDto(
    Guid LeadId,
    bool OfferRequested,
    bool OfferApplied);
