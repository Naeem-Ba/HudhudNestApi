using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Domain.Valuation.Entities;

/// <summary>
/// An agency's estimate submitted in response to one <see cref="ValuationOfficeInvitation"/>.
/// DDD: private setters + factory method — no state machine needed here (unlike Inquiry and
/// Invitation, a response has no lifecycle of its own once submitted; the *invitation* is
/// what moves to Responded, via <see cref="ValuationOfficeInvitation.MarkResponded"/>).
/// </summary>
public sealed class ValuationOfficeResponse : BaseEntity
{
    private ValuationOfficeResponse() { }

    public Guid InvitationId { get; private set; }

    /// <summary>
    /// Same type as every price field on Property (ColdRent/WarmRent/PurchasePrice, etc.):
    /// decimal, never double/float. Required and must be strictly positive — a zero or
    /// negative estimate is not a real valuation.
    /// </summary>
    public decimal EstimatedPrice { get; private set; }

    /// <summary>Optional free-text remark. No length limit — no existing Domain rule caps a
    /// similar free-text field (e.g. ServiceRequest.ProviderNote/RequesterNote), so none is
    /// invented here either.</summary>
    public string? Notes { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    public static ValuationOfficeResponse Create(
        Guid invitationId,
        decimal estimatedPrice,
        DateTime utcNow,
        string? notes = null)
    {
        if (invitationId == Guid.Empty)
            throw new DomainException("معرّف الدعوة مطلوب.");

        if (estimatedPrice <= 0)
            throw new DomainException("السعر المقدَّر يجب أن يكون أكبر من صفر.");

        return new ValuationOfficeResponse
        {
            InvitationId = invitationId,
            EstimatedPrice = estimatedPrice,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            SubmittedAt = utcNow,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }
}
