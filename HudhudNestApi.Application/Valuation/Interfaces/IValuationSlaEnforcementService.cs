using HudhudNestApi.Application.Valuation.DTOs;

namespace HudhudNestApi.Application.Valuation.Interfaces;

/// <summary>
/// Stage 5 — enforces ValuationInquiry's 24h SLA (<see cref="Domain.Valuation.Entities.ValuationInquiry.DefaultExpiryWindow"/>):
/// nothing in this module is ever left "pending" past its window. One sweep does two
/// independent things, in order:
///   1. Expires any non-terminal inquiry whose ExpiresAt has passed, notifying its requester
///      (if not anonymous).
///   2. Expires any still-Sent office invitation that no longer needs a response — either
///      because its own parent inquiry's window closed, or because that inquiry already
///      finished by some other path — notifying the invited agency's owner.
///
/// A plain orchestration service, not a MediatR handler (same reasoning as
/// IOfficeMatchingService/IPropertyOwnershipService) — called from
/// ValuationInquiryExpiryHostedService's scoped work, not from an HTTP request.
/// </summary>
public interface IValuationSlaEnforcementService
{
    Task<ValuationSlaSweepResult> RunSweepAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default);
}
