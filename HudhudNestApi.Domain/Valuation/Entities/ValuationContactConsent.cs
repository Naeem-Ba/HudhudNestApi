using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Domain.Valuation.Entities;

/// <summary>
/// Stage 9 — the customer's explicit, one-time authorization to share their contact details
/// with ONE specific office, for ONE specific inquiry: "after seeing this office's estimate, I
/// choose to let them contact me."
///
/// Deliberately a NEW, narrow entity rather than a reuse of Users.Entities.ConsentRecord
/// (this codebase's existing consent concept). ConsentRecord's whole shape — (UserId,
/// PolicyType, PolicyVersion) — models "this logged-in account agreed to version X of a global
/// policy document"; it requires a real UserId and has no field for "which inquiry, which
/// office, what contact details." Reusing it would mean stretching PolicyVersion into an
/// inquiry/invitation reference (abusing a field whose entire purpose is a document version
/// string) and would silently break every guest inquiry (ValuationInquiry.RequesterId is
/// nullable by design — see its own doc comment — but ConsentRecord.UserId is not). This is a
/// different consent concept — "may THIS transaction's contact info be shared" vs. "did this
/// account accept this policy" — the same reasoning ValuationOfficeInvitationStatus's own doc
/// comment gives for not reusing AgencyInvitationStatus despite both being "an invitation".
///
/// Deliberately BaseEntity, not AuditableEntity, and with no revocation/lifecycle: this is a
/// one-shot, create-once fact ("the customer consented, on this date, to share this contact
/// info with this office") — same "no *ByUserId columns needed, no state machine needed"
/// reasoning ValuationOfficeResponse's own doc comment already gives for itself.
/// </summary>
public sealed class ValuationContactConsent : BaseEntity
{
    private ValuationContactConsent() { }

    public Guid InquiryId { get; private set; }

    public Guid InvitationId { get; private set; }

    /// <summary>
    /// Denormalized copy of the invitation's own AgencyId — same reasoning
    /// ValuationOfficeInvitation.AgencyId itself gives for not requiring a join back to the
    /// invitation just to answer "does agency X have consent for inquiry Y": the office
    /// dashboard's per-agency, per-invitation read path needs this directly.
    /// </summary>
    public Guid AgencyId { get; private set; }

    /// <summary>Same max length as Agency.ContactPhone, for consistency.</summary>
    public string? ContactPhone { get; private set; }

    /// <summary>Same max length as Agency.ContactEmail, for consistency.</summary>
    public string? ContactEmail { get; private set; }

    public DateTime ConsentedAt { get; private set; }

    public static ValuationContactConsent Create(
        Guid inquiryId,
        Guid invitationId,
        Guid agencyId,
        string? contactPhone,
        string? contactEmail,
        DateTime utcNow)
    {
        if (inquiryId == Guid.Empty)
            throw new DomainException("معرّف طلب التقييم مطلوب.");

        if (invitationId == Guid.Empty)
            throw new DomainException("معرّف دعوة المكتب مطلوب.");

        if (agencyId == Guid.Empty)
            throw new DomainException("معرّف المكتب مطلوب.");

        var phone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
        var email = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();

        // A consent to share nothing is not a real consent — at least one way for the office
        // to actually reach the customer must be provided.
        if (phone is null && email is null)
            throw new DomainException("يجب تقديم رقم هاتف أو بريد إلكتروني واحد على الأقل للمشاركة.");

        return new ValuationContactConsent
        {
            InquiryId = inquiryId,
            InvitationId = invitationId,
            AgencyId = agencyId,
            ContactPhone = phone,
            ContactEmail = email,
            ConsentedAt = utcNow,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }
}
