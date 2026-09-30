using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Domain.Agencies.Entities;

/// <summary>
/// A real-estate office: a named organisation that several users work under.
///
/// SCOPE — read this before extending it. This is an organisational GROUPING, not a
/// tenant. A Property still belongs to its OwnerId exactly as before; Agency only adds
/// an optional AgencyId alongside it, and UserAccount.AgencyId records who works where.
/// There is deliberately NO global query filter on AgencyId and no row-level isolation:
/// every existing query keeps working untouched, and nothing here can silently hide a
/// row from a query that did not ask about agencies.
///
/// That is a real limit, not an oversight. Two agencies' data live in the same tables
/// and are separated by explicit predicates, so a handler that forgets the predicate
/// leaks across agencies. If strict isolation is ever required, the upgrade path is to
/// add a global query filter plus an ambient IAgencyContext — which is exactly the work
/// the architecture review wanted a design spike for, and it is a bigger change than
/// this file.
///
/// Membership roles live in ASP.NET Identity (RoleNames.AgencyOwner / AgencyAgent) so
/// [Authorize(Roles = ...)] keeps working the way it does everywhere else in this API.
/// The role says what a user may do; UserAccount.AgencyId says which agency they do it
/// for. Neither is derivable from the other, so neither duplicates the other.
/// </summary>
public sealed class Agency : AuditableEntity
{
    /// <summary>Maximum members an agency may hold, owner included.</summary>
    public const int MaxMembers = 50;

    private Agency() { }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// URL-safe identifier for the agency's public page (/agencies/{slug}). Unique and
    /// lower-cased. Kept separate from Name so an agency can be renamed without breaking
    /// every link that was ever shared to it.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? LogoUrl { get; private set; }

    /// <summary>
    /// Storage provider id for the logo (Cloudinary PublicId), kept so the old file can
    /// be deleted when a new logo is uploaded — same pattern as PropertyImage.PublicId
    /// and UserAccount.ProfileImagePublicId.
    /// </summary>
    public string? LogoPublicId { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? City { get; private set; }

    /// <summary>ISO 3166-1 alpha-2, matching Property.CountryCode.</summary>
    public string CountryCode { get; private set; } = "SY";

    // ── الموقع الجغرافي المنظَّم (يغلق نفس الفجوة التي أُغلقت على Property) ──
    //
    // نفس نمط Property.GovernorateId/DistrictId/NeighborhoodId بالضبط: public
    // setters، nullable بالكامل، وCity النصي القديم يبقى كما هو للتوافق مع كل
    // مكتب موجود مسبقًا. الأولوية: GovernorateId شبه أساسي، DistrictId أدق،
    // NeighborhoodId الأدق — لكن لا شيء منها إلزامي على مستوى الكيان أو قاعدة
    // البيانات، فمكتب قديم بلا أي منها (null/null/null) يبقى "غير مصنَّف
    // جغرافيًا" وقابلاً للقراءة والتعديل بشكل طبيعي (انظر AgencyConfiguration
    // وCreateAgencyCommandValidator/UpdateAgencyCommandValidator لقواعد
    // الاتساق الهرمي).
    public int? GovernorateId { get; set; }
    public int? DistrictId { get; set; }
    public int? NeighborhoodId { get; set; }

    // Navigation — أحادية الاتجاه فقط (لا ICollection<Agency> مقابلة على
    // Governorate/District/Neighborhood)، لأن لا شيء غير Agency نفسها يحتاج
    // اجتياز هذه العلاقة من الطرف الآخر بعد؛ نفس ما تفعله Property.AgencyId
    // (HasOne<Agency>().WithMany() بلا navigation مقابل) في PropertyConfiguration.
    public Governorate? Governorate { get; set; }
    public District? District { get; set; }
    public Neighborhood? Neighborhood { get; set; }

    /// <summary>
    /// Government/registry licence number as supplied by the agency.
    ///
    /// Stored as free text and NEVER validated, inferred, or auto-filled — it is a legal
    /// identifier, and the architecture review is explicit that legal fields must not be
    /// populated by anything other than a human typing them. Nothing in this codebase
    /// treats a present licence number as proof of anything.
    /// </summary>
    public string? LicenseNumber { get; private set; }

    /// <summary>
    /// The user who owns the agency. Holds RoleNames.AgencyOwner and is the only member
    /// who can add or remove other members. Never null: an agency without an owner has
    /// nobody who can administer it.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Whether the agency is publicly listed. Deactivating hides the agency page and
    /// blocks new members, but does NOT touch the listings its members own — those
    /// belong to the members, not to the agency.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    // ── مراجعة إدارية (Stage 8 — Admin Dashboard) ──
    //
    // نفس نمط IsActive/Activate/Deactivate أعلاه بالضبط: علم منطقي بسيط +
    // طابع زمني، بلا حالة وسيطة. لا يوجد نظام Flag/Moderation/Review عام في
    // هذا المشروع (تم البحث في كامل الحل قبل إضافته) — فهذا أقرب نمط موجود
    // فعليًا على هذا الكيان بالذات، وليس نظامًا منفصلًا جديدًا. تعليم المكتب
    // لا يمنعه من العمل (لا علاقة له بـ IsActive) — إنه فقط علم مرئي للأدمن
    // يشير إلى "هذا المكتب يحتاج مراجعة يدوية" (مثلاً بطء الاستجابة المتكرر).
    public bool RequiresManualReview { get; private set; }

    public string? ManualReviewReason { get; private set; }

    public DateTime? ManualReviewFlaggedAt { get; private set; }

    public static Agency Create(
        string name,
        string slug,
        Guid ownerUserId,
        string countryCode,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("اسم المكتب العقاري مطلوب.");

        if (string.IsNullOrWhiteSpace(slug))
            throw new DomainException("المعرّف المختصر للمكتب مطلوب.");

        if (ownerUserId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء مكتب عقاري بلا مالك.");

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
            throw new DomainException("رمز الدولة يجب أن يكون حرفين وفق ISO 3166-1.");

        return new Agency
        {
            Name = name.Trim(),
            Slug = NormalizeSlug(slug),
            OwnerUserId = ownerUserId,
            CountryCode = countryCode.Trim().ToUpperInvariant(),
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void UpdateProfile(
        string name,
        string? description,
        string? contactEmail,
        string? contactPhone,
        string? city,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("اسم المكتب العقاري مطلوب.");

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Records the licence number a human typed. Separate from UpdateProfile so the write
    /// is deliberate and greppable: this is the one field on the entity that carries legal
    /// weight, and it should never be swept along by a bulk profile save.
    /// </summary>
    public void SetLicenseNumber(string? licenseNumber, DateTime utcNow)
    {
        LicenseNumber = string.IsNullOrWhiteSpace(licenseNumber)
            ? null
            : licenseNumber.Trim();

        UpdatedAt = utcNow;
    }

    public void SetLogo(string? logoUrl, string? logoPublicId, DateTime utcNow)
    {
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        LogoPublicId = string.IsNullOrWhiteSpace(logoPublicId) ? null : logoPublicId.Trim();
        UpdatedAt = utcNow;
    }

    public void TransferOwnership(Guid newOwnerUserId, DateTime utcNow)
    {
        if (newOwnerUserId == Guid.Empty)
            throw new DomainException("لا يمكن نقل ملكية المكتب إلى مستخدم غير محدد.");

        if (newOwnerUserId == OwnerUserId)
            throw new DomainException("المستخدم المحدد هو مالك المكتب أصلاً.");

        OwnerUserId = newOwnerUserId;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }

    public void Activate(DateTime utcNow)
    {
        IsActive = true;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Marks the office for manual admin review (e.g. a slow/poor SLA-compliance pattern
    /// surfaced by the Stage 8 dashboard). Purely a visibility flag — it does not deactivate
    /// the agency or block it from receiving new invitations; an admin decides what to do
    /// about a flagged office separately.
    /// </summary>
    public void FlagForManualReview(string reason, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب تعليم المكتب للمراجعة مطلوب.");

        RequiresManualReview = true;
        ManualReviewReason = reason.Trim();
        ManualReviewFlaggedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void ClearManualReviewFlag(DateTime utcNow)
    {
        RequiresManualReview = false;
        ManualReviewReason = null;
        ManualReviewFlaggedAt = null;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Lower-cases, trims, and collapses anything that is not a letter, digit or dash into
    /// a single dash. Arabic letters are kept — a Syrian agency should be able to have an
    /// Arabic slug rather than a transliteration nobody would type.
    /// </summary>
    public static string NormalizeSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("المعرّف المختصر للمكتب مطلوب.");

        var chars = value.Trim().ToLowerInvariant().ToCharArray();
        var builder = new System.Text.StringBuilder(chars.Length);
        var lastWasDash = false;

        foreach (var c in chars)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash || builder.Length == 0)
                continue;

            builder.Append('-');
            lastWasDash = true;
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length == 0)
            throw new DomainException("المعرّف المختصر للمكتب غير صالح.");

        return slug;
    }
}
