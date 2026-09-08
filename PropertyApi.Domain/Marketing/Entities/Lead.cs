using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Marketing.Enums;

namespace PropertyApi.Domain.Marketing.Entities;

/// <summary>
/// A prospective user or agency who expressed interest through a marketing surface (today:
/// the landing-page waitlist form) — structured storage replacing the previous approach of
/// stuffing this data into a <c>ContactMessage.Body</c> free-text field via
/// <c>POST /api/Contact</c>. See landing-page audit notes for why that was a real gap:
/// no queryable city/userType/status, no offer linkage, no campaign attribution.
/// </summary>
public sealed class Lead : BaseEntity
{
    /// <summary>Allow-list mirrored from the frontend's <c>UserType</c> union
    /// (src/app/core/models/lead.model.ts) — kept as a validated lowercase string, not an
    /// enum, matching how <c>Plan.Tier</c> stores its machine-readable tier already.</summary>
    public static readonly IReadOnlyCollection<string> AllowedUserTypes =
        new[] { "renter", "buyer", "owner", "agency", "investor" };

    public string FullName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string UserType { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    /// <summary>Where the visitor came from, e.g. "landing-page". Free-form but short —
    /// lets a future non-landing surface (referral link, in-app share) reuse this entity
    /// without a schema change.</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>UTM-style campaign tag, if the visitor arrived via a tracked link. Optional.</summary>
    public string? Campaign { get; private set; }

    /// <summary>Set only when this lead successfully reserved a redemption slot on an
    /// active Offer at submission time (see Offer.TryReserveRedemption's atomic-update
    /// note) — never set retroactively, so it can be trusted as "this person actually
    /// claimed the offer", not "this person merely saw it".</summary>
    public Guid? OfferId { get; private set; }

    public string? IpAddress { get; private set; }

    public LeadStatus Status { get; private set; } = LeadStatus.New;

    private Lead() { }

    public static Lead Create(
        string fullName,
        string phone,
        string city,
        string userType,
        string source,
        string? notes = null,
        string? campaign = null,
        Guid? offerId = null,
        string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("الاسم الكامل مطلوب.");

        if (string.IsNullOrWhiteSpace(phone))
            throw new DomainException("رقم الهاتف مطلوب.");

        if (string.IsNullOrWhiteSpace(city))
            throw new DomainException("المدينة مطلوبة.");

        var normalizedUserType = userType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedUserTypes.Contains(normalizedUserType))
            throw new DomainException("نوع المستخدم غير معروف.");

        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException("مصدر التسجيل مطلوب.");

        return new Lead
        {
            FullName = fullName.Trim(),
            Phone = phone.Trim(),
            City = city.Trim(),
            UserType = normalizedUserType,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Source = source.Trim(),
            Campaign = string.IsNullOrWhiteSpace(campaign) ? null : campaign.Trim(),
            OfferId = offerId,
            IpAddress = ipAddress,
            Status = LeadStatus.New
        };
    }

    /// <summary>Terminal states (Converted/Rejected) never move again — a lead that already
    /// closed one way or the other is not reopened by re-running the pipeline; a fresh
    /// signup would create a new Lead row instead.</summary>
    public void SetStatus(LeadStatus newStatus)
    {
        if (Status is LeadStatus.Converted or LeadStatus.Rejected)
        {
            throw new DomainException(
                $"لا يمكن تغيير حالة عميل محتمل في حالة نهائية ('{Status}').");
        }

        Status = newStatus;
    }
}
