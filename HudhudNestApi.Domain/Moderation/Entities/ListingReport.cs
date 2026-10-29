using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Domain.Moderation.Entities;

/// <summary>
/// A signed-in user's claim that a listing breaks the rules. A report is a claim to review, never a
/// verdict: nothing here hides a listing or restricts an account. Those are separate admin actions
/// (the existing listing take-down) that the report only records.
///
/// What the reporter sent (listing, reason, details) is fixed at creation and has no setter: the
/// reporter can only add an answer when an admin asks for more information, and the listing owner
/// has no access to the report at all. Status transitions are enforced here and nowhere else.
/// </summary>
public sealed class ListingReport : BaseEntity
{
    private ListingReport() { }

    /// <summary>Database-assigned sequence number behind <see cref="ReferenceNumber"/>.</summary>
    public long Number { get; private set; }

    /// <summary>What the reporter and the admin quote, e.g. RPT-000042.</summary>
    public string ReferenceNumber => $"RPT-{Number:D6}";

    public Guid PropertyId { get; private set; }

    public Guid ReporterUserId { get; private set; }

    /// <summary>
    /// The listing's owner when the report was made. Kept so a report still points at the right
    /// publisher if the listing is later deleted, and so the admin can see other reports against
    /// the same publisher. Never returned to anyone but admins.
    /// </summary>
    public Guid ListingOwnerId { get; private set; }

    /// <summary>The listing title the reporter saw. The owner can edit the title afterwards; the admin needs to know what was reported.</summary>
    public string ListingTitleSnapshot { get; private set; } = string.Empty;

    public ListingReportReason Reason { get; private set; }

    public string? Details { get; private set; }

    public ListingReportStatus Status { get; private set; }

    public ListingReportPriority Priority { get; private set; }

    public Guid? AssignedAdminId { get; private set; }

    /// <summary>The latest question an admin asked the reporter (shown to the reporter).</summary>
    public string? InfoRequest { get; private set; }

    public ListingReportOutcome? Outcome { get; private set; }

    /// <summary>Why the report was closed this way. Internal: never shown to the reporter or the owner.</summary>
    public string? ResolutionNote { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    /// <summary>First time an admin acted on the report: the basis of "time to first review".</summary>
    public DateTime? FirstReviewedAt { get; private set; }

    public int ReopenCount { get; private set; }

    /// <summary>
    /// A random id the client generates once per report dialog. Sending the same report twice (a
    /// double tap, a retry after a lost response) returns the first report instead of a second one.
    /// </summary>
    public Guid? ClientRequestId { get; private set; }

    public bool IsOpen => ListingReportPolicy.IsOpen(Status);

    public static ListingReport Submit(
        Guid reporterUserId,
        Guid propertyId,
        Guid listingOwnerId,
        string listingTitle,
        ListingReportReason reason,
        string? details,
        Guid? clientRequestId,
        DateTime utcNow)
    {
        if (reporterUserId == Guid.Empty)
            throw new DomainException("لا يمكن تقديم بلاغ بلا حساب.");

        if (propertyId == Guid.Empty)
            throw new DomainException("البلاغ يحتاج إعلانًا محددًا.");

        if (!Enum.IsDefined(reason))
            throw new DomainException("سبب البلاغ غير صالح.");

        if (reporterUserId == listingOwnerId)
            throw new DomainException("لا يمكنك الإبلاغ عن إعلانك.");

        var trimmed = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        if (trimmed is { Length: > ListingReportPolicy.MaxDetailsLength })
            throw new DomainException("التفاصيل أطول من المسموح.");

        if (ListingReportPolicy.RequiresDetails(reason) &&
            (trimmed is null || trimmed.Length < ListingReportPolicy.MinDetailsLengthWhenRequired))
            throw new DomainException("اشرح المشكلة في حقل التفاصيل.");

        var title = (listingTitle ?? string.Empty).Trim();
        if (title.Length > ListingReportPolicy.MaxTitleSnapshotLength)
            title = title[..ListingReportPolicy.MaxTitleSnapshotLength];

        return new ListingReport
        {
            ReporterUserId = reporterUserId,
            PropertyId = propertyId,
            ListingOwnerId = listingOwnerId,
            ListingTitleSnapshot = title,
            Reason = reason,
            Details = trimmed,
            Status = ListingReportStatus.New,
            Priority = ListingReportPolicy.DefaultPriority(reason),
            ClientRequestId = clientRequestId == Guid.Empty ? null : clientRequestId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    /// <summary>An admin takes the report into review. Allowed from any open status except UnderReview itself.</summary>
    public void StartReview(DateTime utcNow)
    {
        EnsureOpen("بدء المراجعة");
        if (Status == ListingReportStatus.UnderReview)
            throw new InvalidStateTransitionException("البلاغ قيد المراجعة بالفعل.");

        MoveTo(ListingReportStatus.UnderReview, utcNow);
    }

    /// <summary>An admin asks the reporter a question; the report waits for the answer.</summary>
    public void RequestInfo(string message, DateTime utcNow)
    {
        EnsureOpen("طلب معلومات إضافية");
        if (Status == ListingReportStatus.WaitingForInformation)
            throw new InvalidStateTransitionException("البلاغ ينتظر ردّ المبلّغ بالفعل.");

        var text = RequireText(message, "اكتب ما المطلوب من المبلّغ.");
        InfoRequest = text;
        MoveTo(ListingReportStatus.WaitingForInformation, utcNow);
    }

    /// <summary>The reporter answers an info request. Their original report stays as it was; the answer is added beside it.</summary>
    public void RecordReporterResponse(Guid reporterUserId, DateTime utcNow)
    {
        if (reporterUserId != ReporterUserId)
            throw new DomainException("لا يمكن الرد على بلاغ ليس لك.");

        if (Status != ListingReportStatus.WaitingForInformation)
            throw new InvalidStateTransitionException("لا يمكن إضافة معلومات إلا عندما تطلبها الإدارة.");

        Status = ListingReportStatus.UnderReview;
        UpdatedAt = utcNow;
    }

    /// <summary>The review found something that needs an action before the report can be closed.</summary>
    public void MarkActionRequired(DateTime utcNow)
    {
        EnsureOpen("تحديد أن البلاغ يتطلب إجراء");
        if (Status == ListingReportStatus.ActionRequired)
            throw new InvalidStateTransitionException("البلاغ مصنّف بالفعل كبلاغ يتطلب إجراء.");

        MoveTo(ListingReportStatus.ActionRequired, utcNow);
    }

    public void Assign(Guid? adminUserId, DateTime utcNow)
    {
        EnsureOpen("الإسناد");
        AssignedAdminId = adminUserId == Guid.Empty ? null : adminUserId;
        MarkReviewed(utcNow);
        UpdatedAt = utcNow;
    }

    public void ChangePriority(ListingReportPriority priority, DateTime utcNow)
    {
        if (!Enum.IsDefined(priority))
            throw new DomainException("درجة الأولوية غير صالحة.");

        EnsureOpen("تغيير الأولوية");
        Priority = priority;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Raises the priority to at least <paramref name="floor"/> (used by the "several accounts
    /// reported this listing" signal). Never lowers a priority an admin set.
    /// </summary>
    public bool RaisePriorityTo(ListingReportPriority floor, DateTime utcNow)
    {
        if (!IsOpen || Priority >= floor)
            return false;

        Priority = floor;
        UpdatedAt = utcNow;
        return true;
    }

    /// <summary>Records the decision. The outcome decides Resolved or Dismissed; the note is required.</summary>
    public void Close(ListingReportOutcome outcome, string note, Guid adminUserId, DateTime utcNow)
    {
        EnsureOpen("إغلاق البلاغ");

        if (!Enum.IsDefined(outcome))
            throw new DomainException("نتيجة المعالجة غير صالحة.");

        ResolutionNote = RequireText(note, "اكتب سبب القرار قبل إغلاق البلاغ.");
        Outcome = outcome;
        ResolvedByUserId = adminUserId;
        ResolvedAt = utcNow;
        MoveTo(ListingReportPolicy.IsDismissal(outcome) ? ListingReportStatus.Dismissed : ListingReportStatus.Resolved, utcNow);
    }

    /// <summary>New information arrived: a closed report goes back to review. The previous decision stays in the timeline.</summary>
    public void Reopen(DateTime utcNow)
    {
        if (IsOpen)
            throw new InvalidStateTransitionException("البلاغ مفتوح بالفعل.");

        Outcome = null;
        ResolutionNote = null;
        ResolvedByUserId = null;
        ResolvedAt = null;
        ReopenCount++;
        Status = ListingReportStatus.UnderReview;
        UpdatedAt = utcNow;
    }

    /// <summary>Stamps the first admin action (for "time to first review"). Idempotent.</summary>
    public void MarkReviewed(DateTime utcNow) => FirstReviewedAt ??= utcNow;

    private void MoveTo(ListingReportStatus status, DateTime utcNow)
    {
        Status = status;
        MarkReviewed(utcNow);
        UpdatedAt = utcNow;
    }

    private void EnsureOpen(string action)
    {
        if (!IsOpen)
            throw new InvalidStateTransitionException($"لا يمكن تنفيذ {action}: البلاغ مغلق ({Status}).");
    }

    private static string RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(message);

        var text = value.Trim();
        if (text.Length > ListingReportPolicy.MaxNoteLength)
            throw new DomainException("النص أطول من المسموح.");

        return text;
    }
}
