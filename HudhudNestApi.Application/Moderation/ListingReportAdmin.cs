using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Moderation;
using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Application.Moderation;

// Admin side of listing reports. AdminListingReportsController carries [Authorize(Roles = Admin)]
// on the class, so every query and command here runs for an admin only; the acting admin's id
// always comes from the token, never from a request body.

// ── Queue, stats, detail ─────────────────────────────────────────────────

public sealed record SearchListingReportsQuery(ListingReportFilter Filter)
    : IRequest<ListingReportPageDto<AdminListingReportSummaryDto>>;

public sealed class SearchListingReportsQueryHandler
    : IRequestHandler<SearchListingReportsQuery, ListingReportPageDto<AdminListingReportSummaryDto>>
{
    private readonly IListingReportRepository _reports;
    private readonly ListingReportOptions _options;

    public SearchListingReportsQueryHandler(IListingReportRepository reports, IOptions<ListingReportOptions> options)
    {
        _reports = reports;
        _options = options.Value;
    }

    public async Task<ListingReportPageDto<AdminListingReportSummaryDto>> Handle(SearchListingReportsQuery request, CancellationToken ct)
    {
        var filter = ListingReportPaging.Normalize(request.Filter);
        var now = DateTime.UtcNow;
        var (rows, total) = await _reports.SearchAsync(filter, _options.OverdueCutoffs(now), ct);

        var labels = await _reports.GetUserLabelsAsync(
            rows.SelectMany(r => new[] { r.Report.ReporterUserId, r.Report.AssignedAdminId ?? Guid.Empty }), ct);

        var items = rows.Select(r => ListingReportAdminViews.Summary(r, labels, _options, now)).ToList();
        return new(items, total, filter.Page, filter.PageSize);
    }
}

public sealed record GetListingReportStatsQuery(ListingReportFilter Filter) : IRequest<AdminListingReportStatsDto>;

public sealed class GetListingReportStatsQueryHandler : IRequestHandler<GetListingReportStatsQuery, AdminListingReportStatsDto>
{
    private readonly IListingReportRepository _reports;
    private readonly ListingReportOptions _options;

    public GetListingReportStatsQueryHandler(IListingReportRepository reports, IOptions<ListingReportOptions> options)
    {
        _reports = reports;
        _options = options.Value;
    }

    public async Task<AdminListingReportStatsDto> Handle(GetListingReportStatsQuery request, CancellationToken ct)
    {
        var s = await _reports.GetStatsAsync(ListingReportPaging.Normalize(request.Filter), _options.OverdueCutoffs(DateTime.UtcNow), ct);
        return new(
            s.Total, s.New, s.UnderReview, s.WaitingForInformation, s.ActionRequired,
            s.New + s.UnderReview + s.WaitingForInformation + s.ActionRequired,
            s.Resolved, s.Dismissed, s.HighPriorityOpen, s.Overdue,
            s.AverageResolutionHours is { } h ? Math.Round(h, 1) : null,
            s.ResolvedSampleSize);
    }
}

public sealed record GetListingReportForAdminQuery(Guid ReportId) : IRequest<AdminListingReportDetailDto>;

public sealed class GetListingReportForAdminQueryHandler : IRequestHandler<GetListingReportForAdminQuery, AdminListingReportDetailDto>
{
    private readonly ListingReportAdminDetailBuilder _detail;
    public GetListingReportForAdminQueryHandler(ListingReportAdminDetailBuilder detail) => _detail = detail;

    public Task<AdminListingReportDetailDto> Handle(GetListingReportForAdminQuery request, CancellationToken ct)
        => _detail.BuildAsync(request.ReportId, ct);
}

public sealed record ListListingReportAssigneesQuery : IRequest<IReadOnlyList<UserLabel>>;

public sealed class ListListingReportAssigneesQueryHandler : IRequestHandler<ListListingReportAssigneesQuery, IReadOnlyList<UserLabel>>
{
    private readonly IListingReportRepository _reports;
    public ListListingReportAssigneesQueryHandler(IListingReportRepository reports) => _reports = reports;

    public Task<IReadOnlyList<UserLabel>> Handle(ListListingReportAssigneesQuery request, CancellationToken ct)
        => _reports.ListAdminsAsync(ct);
}

// ── Decisions ────────────────────────────────────────────────────────────

public enum ListingReportAdminAction
{
    StartReview = 1,
    Assign = 2,
    ChangePriority = 3,
    AddNote = 4,
    RequestInfo = 5,
    MarkActionRequired = 6,
    Close = 7,
    Reopen = 8,
}

/// <summary>
/// One admin decision on a report. Which fields are used depends on <see cref="Action"/>:
/// Assign uses <see cref="AssigneeId"/> (null unassigns), ChangePriority uses <see cref="Priority"/>,
/// Close uses <see cref="Outcome"/>; <see cref="Text"/> is the note, question or reason.
/// </summary>
public sealed record ListingReportAdminCommand(
    Guid AdminUserId,
    Guid ReportId,
    ListingReportAdminAction Action,
    string? Text,
    Guid? AssigneeId,
    ListingReportPriority? Priority,
    ListingReportOutcome? Outcome,
    string? IpAddress) : IRequest<AdminListingReportDetailDto>;

public sealed class ListingReportAdminCommandValidator : AbstractValidator<ListingReportAdminCommand>
{
    public ListingReportAdminCommandValidator()
    {
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Text).MaximumLength(ListingReportPolicy.MaxNoteLength);

        RuleFor(x => x.Text).NotEmpty().WithMessage("اكتب نص الملاحظة.")
            .When(x => x.Action == ListingReportAdminAction.AddNote);
        RuleFor(x => x.Text).NotEmpty().WithMessage("اكتب ما المطلوب من المبلّغ.")
            .When(x => x.Action == ListingReportAdminAction.RequestInfo);
        RuleFor(x => x.Text).NotEmpty().WithMessage("اكتب ما الإجراء المطلوب.")
            .When(x => x.Action == ListingReportAdminAction.MarkActionRequired);
        RuleFor(x => x.Text).NotEmpty().WithMessage("اكتب سبب القرار قبل إغلاق البلاغ.")
            .When(x => x.Action == ListingReportAdminAction.Close);
        RuleFor(x => x.Text).NotEmpty().WithMessage("اكتب سبب إعادة فتح البلاغ.")
            .When(x => x.Action == ListingReportAdminAction.Reopen);

        RuleFor(x => x.Priority).NotNull().IsInEnum()
            .When(x => x.Action == ListingReportAdminAction.ChangePriority);
        RuleFor(x => x.Outcome).NotNull().WithMessage("اختر نتيجة المعالجة.").IsInEnum()
            .When(x => x.Action == ListingReportAdminAction.Close);
    }
}

/// <summary>
/// Applies one decision in one transaction under an advisory lock on the report (two admins acting
/// on the same report are serialised, and the xmin token turns any remaining race into a 409):
/// re-read the report, apply the transition the domain allows, append the timeline row, commit,
/// then write the audit row and notify the reporter where the decision concerns them.
/// </summary>
public sealed class ListingReportAdminCommandHandler : IRequestHandler<ListingReportAdminCommand, AdminListingReportDetailDto>
{
    private readonly IListingReportRepository _reports;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly ListingReportNotifier _notifier;
    private readonly ListingReportAdminDetailBuilder _detail;
    private readonly ILogger<ListingReportAdminCommandHandler> _logger;

    public ListingReportAdminCommandHandler(
        IListingReportRepository reports,
        IUnitOfWork uow,
        IAuditLogService audit,
        ListingReportNotifier notifier,
        ListingReportAdminDetailBuilder detail,
        ILogger<ListingReportAdminCommandHandler> logger)
    {
        _reports = reports;
        _uow = uow;
        _audit = audit;
        _notifier = notifier;
        _detail = detail;
        _logger = logger;
    }

    public async Task<AdminListingReportDetailDto> Handle(ListingReportAdminCommand request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        ListingReport report;
        object before;
        ListingReportEvent? recorded;

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireAdvisoryLockAsync(ListingReportLocks.ForReport(request.ReportId), ct);

            report = await _reports.GetByIdAsync(request.ReportId, ct)
                ?? throw new NotFoundException("البلاغ غير موجود.");

            before = ListingReportAdminViews.AuditSnapshot(report);
            recorded = await ApplyAsync(report, request, now, ct);
            if (recorded is not null)
                await _reports.AddEventAsync(recorded, ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }

        if (AuditActionFor(request.Action) is { } action)
        {
            await _audit.LogAsync(
                request.AdminUserId, action, request.IpAddress,
                ListingReportAdminViews.Json(before),
                ListingReportAdminViews.Json(ListingReportAdminViews.AuditSnapshot(report)),
                ct);
        }

        _logger.LogInformation(
            "Listing report updated by admin. ReportId={ReportId}, Action={Action}, Status={Status}",
            report.Id, request.Action, report.Status);

        if (request.Action == ListingReportAdminAction.RequestInfo && recorded is not null)
            await _notifier.NotifyReporterInfoRequestedAsync(report, recorded.Id, request.Text!, ct);
        else if (request.Action == ListingReportAdminAction.Close)
            await _notifier.NotifyReporterClosedAsync(report, ct);

        return await _detail.BuildAsync(report.Id, ct);
    }

    private async Task<ListingReportEvent?> ApplyAsync(
        ListingReport report, ListingReportAdminCommand request, DateTime now, CancellationToken ct)
    {
        var admin = request.AdminUserId;
        var fromStatus = report.Status.ToString();

        switch (request.Action)
        {
            case ListingReportAdminAction.StartReview:
                {
                    report.StartReview(now);
                    // Taking a report into review without an owner makes the reviewer its owner.
                    if (report.AssignedAdminId is null)
                        report.Assign(admin, now);
                    return ListingReportEvent.Record(report.Id, admin, ListingReportEventType.StatusChanged, isInternal: true, now,
                        note: request.Text, fromValue: fromStatus, toValue: report.Status.ToString());
                }

            case ListingReportAdminAction.Assign:
                {
                    var target = request.AssigneeId is { } id && id != Guid.Empty ? id : (Guid?)null;
                    if (target is { } assignee && !await _reports.IsActiveAdminAsync(assignee, ct))
                        throw new ConflictException("لا يمكن إسناد البلاغ إلا إلى حساب إداري نشط.", ListingReportErrorCodes.InvalidAssignee);

                    var previous = report.AssignedAdminId;
                    if (previous == target)
                        return null;

                    report.Assign(target, now);
                    return ListingReportEvent.Record(report.Id, admin,
                        target is null ? ListingReportEventType.Unassigned : ListingReportEventType.Assigned,
                        isInternal: true, now, note: request.Text,
                        fromValue: previous?.ToString(), toValue: target?.ToString());
                }

            case ListingReportAdminAction.ChangePriority:
                {
                    var previous = report.Priority;
                    var next = request.Priority!.Value;
                    if (previous == next)
                        return null;

                    report.ChangePriority(next, now);
                    report.MarkReviewed(now);
                    return ListingReportEvent.Record(report.Id, admin, ListingReportEventType.PriorityChanged, isInternal: true, now,
                        note: request.Text, fromValue: previous.ToString(), toValue: next.ToString());
                }

            case ListingReportAdminAction.AddNote:
                report.MarkReviewed(now);
                report.UpdatedAt = now;
                return ListingReportEvent.Record(report.Id, admin, ListingReportEventType.NoteAdded, isInternal: true, now,
                    note: request.Text);

            case ListingReportAdminAction.RequestInfo:
                report.RequestInfo(request.Text!, now);
                // Not internal: this question is what the reporter is asked to answer.
                return ListingReportEvent.Record(report.Id, admin, ListingReportEventType.InfoRequested, isInternal: false, now,
                    note: request.Text, fromValue: fromStatus, toValue: report.Status.ToString());

            case ListingReportAdminAction.MarkActionRequired:
                report.MarkActionRequired(now);
                return ListingReportEvent.Record(report.Id, admin, ListingReportEventType.StatusChanged, isInternal: true, now,
                    note: request.Text, fromValue: fromStatus, toValue: report.Status.ToString());

            case ListingReportAdminAction.Close:
                {
                    var outcome = request.Outcome!.Value;
                    if (outcome == ListingReportOutcome.ListingTakenDown)
                    {
                        // "Taken down" must be true, not just typed: the listing has to be off the site.
                        var listing = await _reports.GetListingSnapshotAsync(report.PropertyId, now, ct);
                        if (listing is not null && !listing.IsTakenDown && !listing.IsDeleted)
                            throw new ConflictException(
                                "الإعلان ما زال منشورًا. أوقف الإعلان أولًا ثم أغلق البلاغ بهذه النتيجة.",
                                ListingReportErrorCodes.ListingNotTakenDown);
                    }

                    report.Close(outcome, request.Text!, admin, now);
                    return ListingReportEvent.Record(report.Id, admin, ListingReportEventType.Closed, isInternal: true, now,
                        note: request.Text, fromValue: fromStatus, toValue: $"{report.Status}:{outcome}");
                }

            case ListingReportAdminAction.Reopen:
                report.Reopen(now);
                return ListingReportEvent.Record(report.Id, admin, ListingReportEventType.Reopened, isInternal: true, now,
                    note: request.Text, fromValue: fromStatus, toValue: report.Status.ToString());

            default:
                throw new DomainException("إجراء غير معروف.");
        }
    }

    private static string? AuditActionFor(ListingReportAdminAction action) => action switch
    {
        ListingReportAdminAction.StartReview => AuditActions.ListingReportReviewStartedByAdmin,
        ListingReportAdminAction.Assign => AuditActions.ListingReportAssignedByAdmin,
        ListingReportAdminAction.ChangePriority => AuditActions.ListingReportPriorityChangedByAdmin,
        ListingReportAdminAction.RequestInfo => AuditActions.ListingReportInfoRequestedByAdmin,
        ListingReportAdminAction.MarkActionRequired => AuditActions.ListingReportActionRequiredByAdmin,
        ListingReportAdminAction.Close => AuditActions.ListingReportClosedByAdmin,
        ListingReportAdminAction.Reopen => AuditActions.ListingReportReopenedByAdmin,
        // A note is already an append-only, attributed timeline row; it changes no state.
        _ => null,
    };
}

// ── Listing actions from a report ────────────────────────────────────────

/// <summary>
/// Takes the reported listing down, or restores it, through the existing admin listing service
/// (which writes its own audit row and tells the owner the reason, without any reporter in it),
/// then records the action on the report's timeline. The report's status does not change by
/// itself: closing the report is a separate decision with its own outcome and note.
/// </summary>
public sealed record ListingReportListingActionCommand(
    Guid AdminUserId,
    Guid ReportId,
    bool TakeDown,
    string Reason,
    string? IpAddress) : IRequest<AdminListingReportDetailDto>;

public sealed class ListingReportListingActionCommandValidator : AbstractValidator<ListingReportListingActionCommand>
{
    public ListingReportListingActionCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("اكتب السبب الذي سيظهر لصاحب الإعلان.")
            .MaximumLength(Property.MaxTakeDownReasonLength);
    }
}

public sealed class ListingReportListingActionCommandHandler
    : IRequestHandler<ListingReportListingActionCommand, AdminListingReportDetailDto>
{
    private readonly IListingReportRepository _reports;
    private readonly IAdminListingService _listings;
    private readonly IUnitOfWork _uow;
    private readonly ListingReportAdminDetailBuilder _detail;

    public ListingReportListingActionCommandHandler(
        IListingReportRepository reports,
        IAdminListingService listings,
        IUnitOfWork uow,
        ListingReportAdminDetailBuilder detail)
    {
        _reports = reports;
        _listings = listings;
        _uow = uow;
        _detail = detail;
    }

    public async Task<AdminListingReportDetailDto> Handle(ListingReportListingActionCommand request, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(request.ReportId, ct)
            ?? throw new NotFoundException("البلاغ غير موجود.");

        var result = request.TakeDown
            ? await _listings.TakeDownListingAsync(report.PropertyId, request.Reason, request.AdminUserId, request.IpAddress, ct)
            : await _listings.RestoreListingAsync(report.PropertyId, request.Reason, request.AdminUserId, request.IpAddress, ct);
        ThrowIfFailed(result);

        var now = DateTime.UtcNow;
        report.MarkReviewed(now);
        report.UpdatedAt = now;
        await _reports.AddEventAsync(ListingReportEvent.Record(
            report.Id, request.AdminUserId,
            request.TakeDown ? ListingReportEventType.ListingTakenDown : ListingReportEventType.ListingRestored,
            isInternal: true, now, note: request.Reason), ct);
        await _uow.SaveChangesAsync(ct);

        return await _detail.BuildAsync(report.Id, ct);
    }

    private static void ThrowIfFailed(AdminOperationResult result)
    {
        if (result.Succeeded) return;
        if (result.NotFound) throw new NotFoundException("الإعلان غير موجود.");
        if (result.Conflict)
            throw new ConflictException(result.Message.Contains("already", StringComparison.OrdinalIgnoreCase)
                ? "الإعلان موقوف بالفعل."
                : "الإعلان غير موقوف.");
        throw new DomainException(result.Message);
    }
}

// ── Shared ───────────────────────────────────────────────────────────────

internal static class ListingReportPaging
{
    public static ListingReportFilter Normalize(ListingReportFilter f) => f with
    {
        Page = Math.Max(1, f.Page),
        PageSize = Math.Clamp(f.PageSize, 1, 100),
        Search = string.IsNullOrWhiteSpace(f.Search) ? null : f.Search.Trim()[..Math.Min(f.Search.Trim().Length, 100)],
        Sort = Enum.IsDefined(f.Sort) ? f.Sort : ListingReportSort.Newest,
    };
}

internal static class ListingReportAdminViews
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Audit snapshots carry ids, statuses and codes only; no free text and no reporter details.</summary>
    public static object AuditSnapshot(ListingReport r) => new
    {
        reportId = r.Id,
        reference = r.ReferenceNumber,
        propertyId = r.PropertyId,
        status = r.Status.ToString(),
        priority = r.Priority.ToString(),
        assignedAdminId = r.AssignedAdminId,
        outcome = r.Outcome?.ToString(),
        reopenCount = r.ReopenCount,
    };

    public static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);

    public static bool IsOverdue(ListingReport r, ListingReportOptions options, DateTime now) =>
        r.IsOpen && r.CreatedAt + options.ReviewTarget(r.Priority) < now;

    public static string Label(IReadOnlyDictionary<Guid, string> labels, Guid id) =>
        labels.TryGetValue(id, out var name) ? name : "-";

    public static AdminListingReportSummaryDto Summary(
        AdminListingReportRow row, IReadOnlyDictionary<Guid, string> labels, ListingReportOptions options, DateTime now)
    {
        var r = row.Report;
        return new AdminListingReportSummaryDto(
            r.Id,
            r.ReferenceNumber,
            r.PropertyId,
            string.IsNullOrWhiteSpace(row.CurrentListingTitle) ? r.ListingTitleSnapshot : row.CurrentListingTitle!,
            row.ListingType,
            row.ListingDeleted,
            row.ListingTakenDown,
            r.Reason,
            r.Status,
            r.Priority,
            IsOverdue(r, options, now),
            r.ReporterUserId,
            Label(labels, r.ReporterUserId),
            r.AssignedAdminId,
            r.AssignedAdminId is { } a ? Label(labels, a) : null,
            r.CreatedAt,
            r.UpdatedAt,
            ListingReportNotifier.Excerpt(r.Details));
    }
}

/// <summary>Builds the admin detail view of one report (the response of every admin decision too).</summary>
public sealed class ListingReportAdminDetailBuilder
{
    private const int RelatedReportsShown = 20;

    private readonly IListingReportRepository _reports;
    private readonly ListingReportOptions _options;

    public ListingReportAdminDetailBuilder(IListingReportRepository reports, IOptions<ListingReportOptions> options)
    {
        _reports = reports;
        _options = options.Value;
    }

    public async Task<AdminListingReportDetailDto> BuildAsync(Guid reportId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var report = await _reports.GetByIdAsync(reportId, ct)
            ?? throw new NotFoundException("البلاغ غير موجود.");

        var listing = await _reports.GetListingSnapshotAsync(report.PropertyId, now, ct);
        var events = await _reports.ListEventsAsync(report.Id, includeInternal: true, ct);
        var related = await _reports.ListForPropertyAsync(report.PropertyId, report.Id, RelatedReportsShown, ct);
        var (totalByReporter, dismissedByReporter) = await _reports.GetReporterHistoryAsync(report.ReporterUserId, ct);

        var ids = new HashSet<Guid> { report.ReporterUserId, report.ListingOwnerId };
        if (report.AssignedAdminId is { } assigned) ids.Add(assigned);
        if (report.ResolvedByUserId is { } resolver) ids.Add(resolver);
        if (listing is not null) ids.Add(listing.OwnerId);
        foreach (var e in events)
            if (e.ActorUserId is { } actor) ids.Add(actor);
        // Assignment rows carry admin ids as values; name them too.
        foreach (var e in events.Where(e => e.Type is ListingReportEventType.Assigned or ListingReportEventType.Unassigned))
        {
            if (Guid.TryParse(e.FromValue, out var f)) ids.Add(f);
            if (Guid.TryParse(e.ToValue, out var t)) ids.Add(t);
        }
        var labels = await _reports.GetUserLabelsAsync(ids, ct);

        // Counted over every open report on the listing, not just the related ones shown.
        var openOnListing = await _reports.ListOpenForPropertyAsync(report.PropertyId, ct);
        var openCount = openOnListing.Count;
        var distinctOpenReporters = openOnListing.Select(r => r.ReporterUserId).Distinct().Count();

        var row = new AdminListingReportRow(report, listing?.Title, listing?.ListingType, listing?.IsDeleted ?? true, listing?.IsTakenDown ?? false);

        return new AdminListingReportDetailDto(
            ListingReportAdminViews.Summary(row, labels, _options, now),
            report.ListingTitleSnapshot,
            report.Details,
            report.InfoRequest,
            report.Outcome,
            report.ResolutionNote,
            report.ResolvedByUserId is { } rb ? ListingReportAdminViews.Label(labels, rb) : null,
            report.ResolvedAt,
            report.FirstReviewedAt,
            report.ReopenCount,
            report.CreatedAt + _options.ReviewTarget(report.Priority),
            listing is null ? null : new AdminListingSnapshotDto(
                listing.Id, listing.Title, listing.ListingType, listing.Status, listing.IsPublished, listing.IsTakenDown,
                listing.TakenDownReason, listing.IsDeleted, listing.IsPubliclyVisible, listing.Price, listing.CurrencyCode,
                listing.City, listing.PublishedAt, listing.ExpiresAt, listing.OwnerId,
                ListingReportAdminViews.Label(labels, listing.OwnerId)),
            new AdminReporterDto(report.ReporterUserId, ListingReportAdminViews.Label(labels, report.ReporterUserId),
                totalByReporter, dismissedByReporter),
            openCount,
            distinctOpenReporters,
            related.Select(r => new AdminRelatedReportDto(
                r.Id, r.ReferenceNumber, r.Reason, r.Status, r.Priority, r.ReporterUserId == report.ReporterUserId, r.CreatedAt)).ToList(),
            events.Select(e => new AdminListingReportEventDto(
                e.Id, e.Type, e.ActorUserId,
                e.ActorUserId is { } a ? ListingReportAdminViews.Label(labels, a) : null,
                AssignmentLabel(e, e.FromValue, labels), AssignmentLabel(e, e.ToValue, labels),
                e.Note, e.IsInternal, e.CreatedAt)).ToList());
    }

    private static string? AssignmentLabel(ListingReportEvent e, string? value, IReadOnlyDictionary<Guid, string> labels) =>
        e.Type is ListingReportEventType.Assigned or ListingReportEventType.Unassigned && Guid.TryParse(value, out var id)
            ? ListingReportAdminViews.Label(labels, id)
            : value;
}
