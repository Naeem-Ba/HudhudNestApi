using PropertyApi.Domain.Users.Enums;

namespace PropertyApi.Domain.Users.Entities;

/// <summary>
/// Business profile of a platform user.
///
/// Contains profile, localization, and business information only.
/// Authentication and account-security state belong to the Identity layer.
/// </summary>
public sealed class UserAccount
{
    private UserAccount()
    {
    }

    public Guid Id { get; private set; }

    public string FirstName { get; private set; } =
        string.Empty;

    public string LastName { get; private set; } =
        string.Empty;

    public string? DisplayName { get; private set; }

    public string? TaxNumber { get; private set; }

    public string? ProfileImageUrl { get; private set; }

    /// <summary>
    /// معرّف الصورة عند مزوّد التخزين (Cloudinary PublicId حاليًا) — يُخزَّن كي
    /// نستطيع حذف الصورة القديمة من التخزين السحابي عند رفع صورة جديدة، بنفس
    /// النمط المُستخدَم لصور العقارات (PropertyImage.PublicId). يبقى null عندما
    /// يكون ProfileImageUrl قد وصل من مصدر خارجي غير مُتتبَّع عبر IMediaStorageService.
    /// </summary>
    public string? ProfileImagePublicId { get; private set; }

    public string? WhatsAppNumber { get; private set; }

    public string PreferredLanguage { get; private set; } =
        "en";

    public string PreferredCurrency { get; private set; } =
        "EUR";

    public string? CountryCode { get; private set; }

    /// <summary>نبذة تعريفية يكتبها صاحب الحساب عن نفسه/عمله — تُعرض بالملف الشخصي العام.</summary>
    public string? Bio { get; private set; }

    /// <summary>عنوان ومعلومات تواصل حرة (نص) يختار المستخدم عرضها بملفه العام.</summary>
    public string? ContactInfo { get; private set; }

    /// <summary>
    /// المكتب العقاري الذي يعمل تحته هذا المستخدم — null للمستخدم المستقل، وهو
    /// الوضع الافتراضي لكل الحسابات القائمة.
    ///
    /// هذا انتماء تنظيمي لا عزل بيانات: لا يوجد أي فلتر عام على هذا الحقل، والعقار
    /// يبقى مملوكاً لـ OwnerId كما كان. أي استعلام يريد التقييد بالمكتب عليه أن
    /// يذكره صراحةً.
    /// </summary>
    public Guid? AgencyId { get; private set; }

    /// <summary>متى انضم المستخدم إلى مكتبه الحالي — يُمسح عند مغادرته.</summary>
    public DateTime? AgencyJoinedAt { get; private set; }

    /// <summary>
    /// الخطة التي اختارها المستخدم صراحةً (بما فيها الخطة المجانية). null يعني أن
    /// المستخدم لم يختر أي خطة بعد — وهذا يختلف عمداً عن معاملة كل حساب كخطة مجانية
    /// ضمنياً: نشر أول إعلان يتطلب اختياراً صريحاً، لا افتراضاً صامتاً.
    /// </summary>
    public Guid? PlanId { get; private set; }

    public DateTime? PlanSelectedAt { get; private set; }

    /// <summary>
    /// When the current PlanId stops granting benefits. Null means "no fixed end" — always
    /// true for a self-service selection (SelectPlan never sets this; see its doc comment),
    /// and true for an admin activation/extension that deliberately grants an open-ended
    /// period. Only meaningful when PlanStatus is Active; see GetEffectivePlanStatus.
    /// </summary>
    public DateTime? PlanExpiresAt { get; private set; }

    /// <summary>
    /// Admin-controlled kill switch, distinct from time-based expiry — see PlanStatus's doc
    /// comment for why Expired isn't a member of this enum. Defaults to Active because every
    /// row that predates this field (self-service selections with no admin ever involved)
    /// must read exactly as before: in force, no fixed end.
    /// </summary>
    public PlanStatus PlanStatus { get; private set; } = PlanStatus.Active;

    /// <summary>Null until the first SelectPlan/ActivatePlanByAdmin call ever sets PlanId.</summary>
    public PlanActivationSource? PlanActivationSource { get; private set; }

    /// <summary>Set by CancelPlan; cleared again if an admin re-activates/extends afterwards.</summary>
    public DateTime? PlanCancelledAt { get; private set; }

    /// <summary>
    /// The admin who most recently activated, extended, or cancelled this account's plan.
    /// Null for a plan that has only ever been self-service-selected.
    /// </summary>
    public Guid? PlanGrantedByUserId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Set only by <see cref="Anonymize"/> as part of account deletion (see that method's doc
    /// comment). Deliberately NOT wired into a global EF Core query filter (see
    /// UserAccountConfiguration.cs and docs/DATABASE-PRODUCTION-READINESS.md Finding F2): every
    /// FK from Property/PropertyReview/VisitRequest/Transaction into UserAccounts is a
    /// *required* relationship, and EF Core turns a global filter on the target of a required
    /// navigation into an INNER JOIN -- which would silently drop a deleted user's retained
    /// properties/reviews from every listing instead of just anonymizing the owner's name.
    /// Read this flag explicitly wherever a UserAccounts row is queried as the primary subject
    /// (a public profile, a directory/member listing) rather than as an incidental navigation
    /// off another entity.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// When the account owner asked to delete their account (F7 GDPR erasure flow). Null means
    /// no deletion is currently pending. Kept even after the deletion actually executes (see
    /// <see cref="Anonymize"/>) as the historical record of when the request was made.
    /// </summary>
    public DateTime? DeletionRequestedAt { get; private set; }

    /// <summary>
    /// When <see cref="RequestDeletion"/> scheduled the account for anonymization -- a
    /// background sweep (Infrastructure layer) picks up any account where this is in the past.
    /// Null means no deletion is currently pending.
    /// Cleared by <see cref="CancelDeletionRequest"/>; left set (not cleared) once the deletion
    /// actually executes, for the same historical-record reason as
    /// <see cref="DeletionRequestedAt"/>.
    /// </summary>
    public DateTime? DeletionScheduledFor { get; private set; }

    /// <summary>
    /// True while a deletion request is outstanding and has not yet executed or been
    /// cancelled -- the account remains fully usable during this window (that is what makes
    /// cancellation meaningful); only the scheduled sweep, once <see cref="DeletionScheduledFor"/>
    /// arrives, calls <see cref="Anonymize"/>.
    /// </summary>
    public bool HasPendingDeletionRequest => DeletionScheduledFor is not null && !IsDeleted;

    /// <summary>
    /// Starts the account-deletion delay window (Finding F7,
    /// docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). Idempotent by design: calling this again
    /// while a request is already pending does not push the date further out -- a user who
    /// clicks "delete my account" twice must not be able to (accidentally or not) extend their
    /// own grace period indefinitely; they get the schedule from the *first* request.
    /// </summary>
    public DateTime RequestDeletion(TimeSpan delay, DateTime utcNow)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException(
                "Cannot request deletion for an account that is already deleted.");
        }

        if (HasPendingDeletionRequest)
        {
            return DeletionScheduledFor!.Value;
        }

        if (delay <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Deletion delay must be positive.",
                nameof(delay));
        }

        DeletionRequestedAt = utcNow;
        DeletionScheduledFor = utcNow.Add(delay);
        UpdatedAt = utcNow;

        return DeletionScheduledFor.Value;
    }

    /// <summary>
    /// Cancels a pending deletion request during its delay window. The account was never
    /// touched by <see cref="Anonymize"/> (that only runs once the sweep actually executes a
    /// matured request), so cancelling is exactly "clear the schedule" -- there is nothing to
    /// restore.
    /// </summary>
    public void CancelDeletionRequest(DateTime utcNow)
    {
        if (!HasPendingDeletionRequest)
        {
            throw new InvalidOperationException(
                "There is no pending deletion request to cancel.");
        }

        DeletionRequestedAt = null;
        DeletionScheduledFor = null;
        UpdatedAt = utcNow;
    }

    public static UserAccount Create(
        Guid id,
        string firstName,
        string lastName,
        DateTime utcNow)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "User account id is required.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "First name is required.",
                nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException(
                "Last name is required.",
                nameof(lastName));
        }

        return new UserAccount
        {
            Id =
                id,

            FirstName =
                firstName.Trim(),

            LastName =
                lastName.Trim(),

            CreatedAt =
                utcNow,

            UpdatedAt =
                utcNow
        };
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string? displayName,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "First name is required.",
                nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException(
                "Last name is required.",
                nameof(lastName));
        }

        FirstName =
            firstName.Trim();

        LastName =
            lastName.Trim();

        DisplayName =
            string.IsNullOrWhiteSpace(displayName)
                ? null
                : displayName.Trim();

        UpdatedAt =
            utcNow;
    }

    public void UpdateProfileImage(
        string? profileImageUrl,
        string? profileImagePublicId,
        DateTime utcNow)
    {
        ProfileImageUrl =
            string.IsNullOrWhiteSpace(profileImageUrl)
                ? null
                : profileImageUrl.Trim();

        ProfileImagePublicId =
            string.IsNullOrWhiteSpace(profileImagePublicId)
                ? null
                : profileImagePublicId.Trim();

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// يحدّث النبذة التعريفية ومعلومات العنوان/التواصل — نفس نمط "null تعني
    /// لا تغيير" المُستخدَم بالفعل في UpdateUserCommandHandler، لكن هنا القيمة
    /// المُرسَلة نفسها تُحدَّد صراحةً من الطبقة الأعلى (المُتصل مسؤول عن حساب
    /// effectiveBio/effectiveContactInfo قبل الاستدعاء)، فهذه الدالة لا تفرّق
    /// بين null و"مسح الحقل" — أي قيمة فارغة أو بيضاء هنا تُخزَّن كـ null فعليًا.
    /// </summary>
    public void UpdateAboutInfo(
        string? bio,
        string? contactInfo,
        DateTime utcNow)
    {
        Bio =
            string.IsNullOrWhiteSpace(bio)
                ? null
                : bio.Trim();

        ContactInfo =
            string.IsNullOrWhiteSpace(contactInfo)
                ? null
                : contactInfo.Trim();

        UpdatedAt =
            utcNow;
    }

    public void UpdatePreferences(
        string preferredLanguage,
        string preferredCurrency,
        string? countryCode,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(
                preferredLanguage))
        {
            throw new ArgumentException(
                "Preferred language is required.",
                nameof(preferredLanguage));
        }

        if (string.IsNullOrWhiteSpace(
                preferredCurrency))
        {
            throw new ArgumentException(
                "Preferred currency is required.",
                nameof(preferredCurrency));
        }

        PreferredLanguage =
            preferredLanguage
                .Trim()
                .ToLowerInvariant();

        PreferredCurrency =
            preferredCurrency
                .Trim()
                .ToUpperInvariant();

        CountryCode =
            string.IsNullOrWhiteSpace(countryCode)
                ? null
                : countryCode
                    .Trim()
                    .ToUpperInvariant();

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// يربط الحساب بمكتب عقاري. يرفض الانضمام إذا كان المستخدم عضواً في مكتب آخر
    /// بالفعل: عضوية واحدة في كل وقت، وإلا صار سؤال "أي مكتب يمثّله هذا الوسيط في
    /// هذا الإعلان؟" بلا جواب واحد.
    /// </summary>
    public void JoinAgency(
        Guid agencyId,
        DateTime utcNow)
    {
        if (agencyId == Guid.Empty)
        {
            throw new ArgumentException(
                "Agency id is required.",
                nameof(agencyId));
        }

        if (AgencyId is not null && AgencyId != agencyId)
        {
            throw new InvalidOperationException(
                "User already belongs to another agency.");
        }

        AgencyId =
            agencyId;

        AgencyJoinedAt =
            utcNow;

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// يفكّ ارتباط الحساب بمكتبه. لا يمسّ عقارات المستخدم إطلاقاً — هي مملوكة له لا
    /// للمكتب، وتبقى منشورة بعد مغادرته.
    /// </summary>
    public void LeaveAgency(
        DateTime utcNow)
    {
        AgencyId =
            null;

        AgencyJoinedAt =
            null;

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// يسجّل اختيار المستخدم لخطة — بما فيها الخطة المجانية. يمكن استدعاؤها أكثر من
    /// مرة (تغيير الخطة لاحقاً)؛ كل استدعاء يحدّث PlanSelectedAt إلى وقت الاختيار
    /// الأخير الفعلي.
    /// </summary>
    public void SelectPlan(
        Guid planId,
        DateTime utcNow)
    {
        if (planId == Guid.Empty)
        {
            throw new ArgumentException(
                "Plan id is required.",
                nameof(planId));
        }

        PlanId =
            planId;

        PlanSelectedAt =
            utcNow;

        // Self-service always means "in force, no fixed end, no admin involved" — even when
        // this call is replacing a prior admin grant (a user picking a new plan themselves
        // supersedes whatever an admin had set up for them before).
        PlanExpiresAt =
            null;

        PlanStatus =
            Enums.PlanStatus.Active;

        PlanActivationSource =
            Enums.PlanActivationSource.SelfService;

        PlanCancelledAt =
            null;

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// Admin grants a plan to this account for free — no payment involved. Unlike
    /// <see cref="SelectPlan"/>, the admin chooses the expiry explicitly (Plan carries no
    /// billing-cycle field by design; see Plan's doc comment), so callers must resolve and
    /// pass it themselves — e.g. utcNow.AddDays(durationDays).
    /// </summary>
    public void ActivatePlanByAdmin(
        Guid planId,
        DateTime? expiresAtUtc,
        Guid adminUserId,
        DateTime utcNow)
    {
        if (planId == Guid.Empty)
        {
            throw new ArgumentException(
                "Plan id is required.",
                nameof(planId));
        }

        if (adminUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Performing admin id is required.",
                nameof(adminUserId));
        }

        if (expiresAtUtc is { } expiry && expiry <= utcNow)
        {
            throw new ArgumentException(
                "Plan expiry must be in the future.",
                nameof(expiresAtUtc));
        }

        PlanId =
            planId;

        PlanSelectedAt =
            utcNow;

        PlanExpiresAt =
            expiresAtUtc;

        PlanStatus =
            Enums.PlanStatus.Active;

        PlanActivationSource =
            Enums.PlanActivationSource.AdminGrant;

        PlanCancelledAt =
            null;

        PlanGrantedByUserId =
            adminUserId;

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// Admin extends the current plan by <paramref name="period"/>. Requires a plan to
    /// already be selected — there is nothing to extend otherwise (use
    /// <see cref="ActivatePlanByAdmin"/> for a first grant).
    ///
    /// Base date: the current PlanExpiresAt if the plan is still Active AND that date is
    /// still in the future (an active, not-yet-expired plan keeps its remaining time and
    /// gets the extension added on top); utcNow otherwise — i.e. an expired or cancelled
    /// plan restarts from the moment the admin acts, per the spec's explicit rule, and this
    /// call reactivates it (PlanStatus back to Active).
    /// </summary>
    public void ExtendPlan(
        TimeSpan period,
        Guid adminUserId,
        DateTime utcNow)
    {
        if (PlanId is null)
        {
            throw new InvalidOperationException(
                "Cannot extend a plan that was never selected.");
        }

        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Extension period must be positive.",
                nameof(period));
        }

        if (adminUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Performing admin id is required.",
                nameof(adminUserId));
        }

        var stillActiveAndFuture =
            PlanStatus == Enums.PlanStatus.Active
            && PlanExpiresAt is { } currentExpiry
            && currentExpiry > utcNow;

        var baseDate =
            stillActiveAndFuture
                ? PlanExpiresAt!.Value
                : utcNow;

        PlanExpiresAt =
            baseDate.Add(period);

        PlanStatus =
            Enums.PlanStatus.Active;

        PlanActivationSource =
            Enums.PlanActivationSource.AdminGrant;

        PlanCancelledAt =
            null;

        PlanGrantedByUserId =
            adminUserId;

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// Admin cancels the current plan, effective immediately (see spec — no billing cycle
    /// exists anywhere in this codebase to define an "at period end" alternative).
    /// Deliberately does NOT null PlanId/PlanExpiresAt: the cancelled plan stays inspectable
    /// on the row itself in addition to the audit-log trail the caller writes separately.
    /// </summary>
    public void CancelPlan(
        Guid adminUserId,
        DateTime utcNow)
    {
        if (PlanId is null)
        {
            throw new InvalidOperationException(
                "Cannot cancel a plan that was never selected.");
        }

        if (adminUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Performing admin id is required.",
                nameof(adminUserId));
        }

        PlanStatus =
            Enums.PlanStatus.Cancelled;

        PlanCancelledAt =
            utcNow;

        PlanGrantedByUserId =
            adminUserId;

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// Scrubs every personal-data field this profile carries, in place, as part of account
    /// deletion (see DeleteUserCommandHandler). This is deliberately NOT a hard delete of the
    /// row: Property.OwnerId, Message.SenderId/ReceiverId, PropertyReview.ReviewerId,
    /// UserRating.RatedUserId/RaterId, VisitRequest.RequesterId, Agency.OwnerUserId and others
    /// all reference UserAccounts with DeleteBehavior.Restrict (see their configurations) —
    /// the row must keep existing so that business/marketplace data other people rely on
    /// (listings, reviews, message history, ratings) stays intact and referentially valid.
    ///
    /// FirstName/LastName cannot become empty (both are NOT NULL and validated non-blank by
    /// Create/UpdateProfile), so they take a fixed, unmistakable placeholder instead of being
    /// merely cleared — anything reading OwnerName/ReviewerName etc. after this call renders
    /// "Deleted User" rather than blank text or a stale real name.
    ///
    /// AgencyId/PlanId/PlanGrantedByUserId and their timestamps are left untouched: they are
    /// business/billing state, not personal data, and changing them here would silently affect
    /// agency membership counts or plan bookkeeping as a side effect of what the caller asked
    /// for (account deletion) — see docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md for why this
    /// is flagged as a business decision rather than resolved unilaterally. IsDeleted is set
    /// here for exactly this reason: callers that need to stop counting/listing this account as
    /// an active agency member (see AgencyRepository.GetMembersAsync/CountMembersAsync) can
    /// check it explicitly, without this method having to also clear AgencyId itself.
    ///
    /// Caller contract: read ProfileImagePublicId *before* calling this (it is cleared here) if
    /// the caller needs it to delete the asset from Cloudinary — this method only updates the
    /// database row, it has no knowledge of external storage.
    /// </summary>
    public void Anonymize(DateTime utcNow)
    {
        FirstName =
            "Deleted";

        LastName =
            "User";

        DisplayName =
            null;

        TaxNumber =
            null;

        ProfileImageUrl =
            null;

        ProfileImagePublicId =
            null;

        WhatsAppNumber =
            null;

        Bio =
            null;

        ContactInfo =
            null;

        IsDeleted =
            true;

        UpdatedAt =
            utcNow;
    }

    /// <summary>
    /// The status callers actually care about — see EffectivePlanStatus's doc comment for
    /// why this is computed rather than a second persisted field.
    /// </summary>
    public EffectivePlanStatus GetEffectivePlanStatus(DateTime asOfUtc)
    {
        if (PlanId is null)
        {
            return EffectivePlanStatus.NoPlan;
        }

        if (PlanStatus == Enums.PlanStatus.Cancelled)
        {
            return EffectivePlanStatus.Cancelled;
        }

        if (PlanExpiresAt is { } expiresAt && expiresAt <= asOfUtc)
        {
            return EffectivePlanStatus.Expired;
        }

        return EffectivePlanStatus.Active;
    }

    /// <summary>
    /// True only when the plan is genuinely in force right now — the check
    /// IListingQuotaPolicy uses to decide whether to trust PlanId's own limit or fall back
    /// to the free tier's.
    /// </summary>
    public bool HasActivePlanBenefits(DateTime asOfUtc)
        => GetEffectivePlanStatus(asOfUtc) == EffectivePlanStatus.Active;
}