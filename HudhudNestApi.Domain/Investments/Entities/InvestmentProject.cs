using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Domain.Investments.Entities;

/// <summary>
/// An investment discovery project — a real-estate investment opportunity users can browse,
/// analyze, and express interest in. DDD: private setters + factory + explicit state-machine
/// methods, same shape as <c>Property</c>/<c>VisitRequest</c>/<c>ShortStayListing</c>.
///
/// Phase 1 guardrail (see docs/investment/PHASE-1-DISCOVERY.md): this entity never represents
/// a real investment, payment, or commitment. It is discovery/marketing content plus a
/// publishing workflow. No money ever moves because this entity exists or changes state.
/// </summary>
public sealed class InvestmentProject : AuditableEntity
{
    // ── References ────────────────────────────────────────────────
    /// <summary>The existing real-estate asset this project is built around. Never duplicated —
    /// address/coordinates/images are always read through this reference.</summary>
    public Guid PropertyId { get; private set; }

    /// <summary>The staff/agent user responsible for managing this project. Distinct from
    /// <see cref="AuditableEntity.CreatedByUserId"/>, which only records who technically created
    /// the row — ownership can be reassigned without rewriting history.</summary>
    public Guid OwnerUserId { get; private set; }

    // ── Content ───────────────────────────────────────────────────
    public string Title { get; private set; } = string.Empty;
    public string? ShortDescription { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public InvestmentProjectType ProjectType { get; private set; }

    // ── Status ────────────────────────────────────────────────────
    public InvestmentProjectStatus Status { get; private set; } = InvestmentProjectStatus.Draft;
    public string? RejectionReason { get; private set; }

    /// <summary>Denormalized copy of the latest <c>InvestmentRiskAssessment.RiskLevel</c> for this
    /// project, kept in sync by <see cref="SetOverallRiskLevel"/>. Exists purely so list/filter
    /// queries can filter/sort by risk level without joining to the risk-assessment table for
    /// every row (Phase 1 spec §32 performance rule).</summary>
    public InvestmentRiskLevel? RiskLevel { get; private set; }

    // ── Investment parameters ────────────────────────────────────
    public decimal TargetAmount { get; private set; }
    public decimal MinimumInvestment { get; private set; }
    public decimal? MaximumInvestment { get; private set; }
    public string Currency { get; private set; } = "USD";
    public int InvestmentTermMonths { get; private set; }
    public decimal ExpectedReturnMin { get; private set; }
    public decimal ExpectedReturnMax { get; private set; }

    /// <summary>Amount raised so far, as tracked/entered by staff. Phase 1 has no payment
    /// pipeline, so this is informational only — never derived from real transactions.</summary>
    public decimal RaisedAmount { get; private set; }

    // ── Schedule ──────────────────────────────────────────────────
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateTime? ScheduledPublishAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    private InvestmentProject() { }

    public static InvestmentProject Create(
        Guid propertyId,
        Guid ownerUserId,
        string title,
        string description,
        InvestmentProjectType projectType,
        string currency = "USD")
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("يجب ربط المشروع الاستثماري بعقار موجود.");

        if (ownerUserId == Guid.Empty)
            throw new DomainException("مسؤول المشروع مطلوب.");

        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان المشروع الاستثماري مطلوب.");

        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("وصف المشروع الاستثماري مطلوب.");

        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("عملة المشروع مطلوبة.");

        return new InvestmentProject
        {
            PropertyId = propertyId,
            OwnerUserId = ownerUserId,
            Title = title.Trim(),
            Description = description.Trim(),
            ProjectType = projectType,
            Currency = currency.Trim().ToUpperInvariant(),
            Status = InvestmentProjectStatus.Draft,
        };
    }

    // ── Content / parameter updates (editable while Draft or Rejected) ─────────────

    public void UpdateContent(string title, string? shortDescription, string description, InvestmentProjectType projectType)
    {
        EnsureEditable();

        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("عنوان المشروع الاستثماري مطلوب.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("وصف المشروع الاستثماري مطلوب.");

        Title = title.Trim();
        ShortDescription = string.IsNullOrWhiteSpace(shortDescription) ? null : shortDescription.Trim();
        Description = description.Trim();
        ProjectType = projectType;
    }

    public void UpdateInvestmentParameters(
        decimal targetAmount,
        decimal minimumInvestment,
        decimal? maximumInvestment,
        string currency,
        int investmentTermMonths,
        decimal expectedReturnMin,
        decimal expectedReturnMax)
    {
        EnsureEditable();

        if (targetAmount <= 0)
            throw new DomainException("المبلغ المستهدف يجب أن يكون أكبر من صفر.");
        if (minimumInvestment <= 0)
            throw new DomainException("الحد الأدنى للاستثمار يجب أن يكون أكبر من صفر.");
        if (maximumInvestment is { } max && max < minimumInvestment)
            throw new DomainException("الحد الأقصى للاستثمار يجب أن يكون أكبر من أو يساوي الحد الأدنى.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("عملة المشروع مطلوبة.");
        if (investmentTermMonths <= 0)
            throw new DomainException("مدة المشروع بالأشهر يجب أن تكون أكبر من صفر.");
        if (expectedReturnMin < 0)
            throw new DomainException("العائد المتوقع (الأدنى) لا يمكن أن يكون سالبًا.");
        if (expectedReturnMax < expectedReturnMin)
            throw new DomainException("العائد المتوقع (الأعلى) يجب أن يكون أكبر من أو يساوي العائد الأدنى.");

        TargetAmount = targetAmount;
        MinimumInvestment = minimumInvestment;
        MaximumInvestment = maximumInvestment;
        Currency = currency.Trim().ToUpperInvariant();
        InvestmentTermMonths = investmentTermMonths;
        ExpectedReturnMin = expectedReturnMin;
        ExpectedReturnMax = expectedReturnMax;
    }

    public void UpdateSchedule(DateOnly? startDate, DateOnly? endDate)
    {
        EnsureEditable();

        if (startDate is { } start && endDate is { } end && end <= start)
            throw new DomainException("تاريخ انتهاء المشروع يجب أن يكون بعد تاريخ البدء.");

        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>Informational only — staff-entered progress figure, never derived from a real
    /// payment. See class-level guardrail comment.</summary>
    public void UpdateRaisedAmount(decimal raisedAmount)
    {
        if (raisedAmount < 0)
            throw new DomainException("المبلغ المجمّع لا يمكن أن يكون سالبًا.");
        if (raisedAmount > TargetAmount)
            throw new DomainException("المبلغ المجمّع لا يمكن أن يتجاوز المبلغ المستهدف.");

        RaisedAmount = raisedAmount;
    }

    /// <summary>Called by the application layer whenever the project's
    /// <c>InvestmentRiskAssessment</c> is created/updated, to keep the denormalized filter field
    /// in sync. Not a public workflow action in its own right.</summary>
    public void SetOverallRiskLevel(InvestmentRiskLevel riskLevel) => RiskLevel = riskLevel;

    // ── State machine ─────────────────────────────────────────────

    public void SubmitForReview()
    {
        EnsureStatus(
            [InvestmentProjectStatus.Draft, InvestmentProjectStatus.Rejected],
            "إرسال المشروع للمراجعة");
        Status = InvestmentProjectStatus.UnderReview;
        RejectionReason = null;
    }

    public void Approve()
    {
        EnsureStatus([InvestmentProjectStatus.UnderReview], "اعتماد المشروع");
        Status = InvestmentProjectStatus.Approved;
    }

    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب الرفض مطلوب.");

        EnsureStatus([InvestmentProjectStatus.UnderReview], "رفض المشروع");
        Status = InvestmentProjectStatus.Rejected;
        RejectionReason = reason.Trim();
    }

    public void Schedule(DateTime? scheduledPublishAt)
    {
        if (scheduledPublishAt is { } at && at < DateTime.UtcNow)
            throw new DomainException("موعد النشر المجدول يجب أن يكون في المستقبل.");

        EnsureStatus([InvestmentProjectStatus.Approved], "جدولة المشروع للنشر");
        Status = InvestmentProjectStatus.Scheduled;
        ScheduledPublishAt = scheduledPublishAt;
    }

    /// <summary>
    /// Transitions Scheduled → Published. Cross-aggregate publish-readiness checks (financials
    /// present, risk assessment present, required public documents present) are enforced by
    /// PublishInvestmentProjectCommandHandler, not here — this aggregate cannot see sibling
    /// aggregates (Phase 1 spec §17 "Publish" rules).
    /// </summary>
    public void Publish()
    {
        EnsureStatus([InvestmentProjectStatus.Scheduled], "نشر المشروع");
        Status = InvestmentProjectStatus.Published;
        PublishedAt = DateTime.UtcNow;
    }

    public void Suspend()
    {
        EnsureStatus([InvestmentProjectStatus.Published], "تعليق المشروع");
        Status = InvestmentProjectStatus.Suspended;
    }

    public void Close()
    {
        EnsureStatus(
            [InvestmentProjectStatus.Published, InvestmentProjectStatus.Suspended],
            "إغلاق المشروع");
        Status = InvestmentProjectStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    public bool IsPubliclyVisible => Status == InvestmentProjectStatus.Published;

    private void EnsureEditable()
    {
        if (Status is not (InvestmentProjectStatus.Draft or InvestmentProjectStatus.Rejected))
            throw new DomainException($"لا يمكن تعديل مشروع في الحالة '{Status}'.");
    }

    private void EnsureStatus(IReadOnlyCollection<InvestmentProjectStatus> allowed, string action)
    {
        if (!allowed.Contains(Status))
            throw new DomainException($"لا يمكن {action} من الحالة '{Status}'.");
    }
}
