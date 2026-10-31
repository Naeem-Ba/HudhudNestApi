using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Moderation;
using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Application.Moderation;

// The reporter's side. Every command takes the reporter's id from the token (the controller never
// reads it from the body), and every lookup is scoped to that id: a foreign report id answers 404.

/// <summary>Stable error codes a localised client can translate.</summary>
public static class ListingReportErrorCodes
{
    public const string OwnListing = "REPORT_OWN_LISTING";
    public const string AlreadyOpen = "REPORT_ALREADY_OPEN";
    public const string RecentlyReviewed = "REPORT_RECENTLY_REVIEWED";
    public const string DailyLimit = "REPORT_DAILY_LIMIT";
    public const string ListingNotAvailable = "REPORT_LISTING_NOT_AVAILABLE";
    public const string ListingNotTakenDown = "REPORT_LISTING_NOT_TAKEN_DOWN";
    public const string InvalidAssignee = "REPORT_INVALID_ASSIGNEE";
}

// ── Reasons ──────────────────────────────────────────────────────────────

public sealed record GetListingReportReasonsQuery : IRequest<IReadOnlyList<ListingReportReasonDto>>;

public sealed class GetListingReportReasonsQueryHandler
    : IRequestHandler<GetListingReportReasonsQuery, IReadOnlyList<ListingReportReasonDto>>
{
    private static readonly IReadOnlyList<ListingReportReasonDto> Reasons = Enum.GetValues<ListingReportReason>()
        .Select(r => new ListingReportReasonDto(r, ListingReportPolicy.RequiresDetails(r), ListingReportPolicy.MaxDetailsLength))
        .ToList();

    public Task<IReadOnlyList<ListingReportReasonDto>> Handle(GetListingReportReasonsQuery request, CancellationToken ct)
        => Task.FromResult(Reasons);
}

// ── Submit ───────────────────────────────────────────────────────────────

public sealed record SubmitListingReportCommand(
    Guid ReporterUserId,
    Guid PropertyId,
    ListingReportReason Reason,
    string? Details,
    Guid? ClientRequestId) : IRequest<SubmitListingReportResult>;

/// <summary><see cref="Created"/> is false when the same dialog's request was replayed and the first report is returned.</summary>
public sealed record SubmitListingReportResult(MyListingReportDto Report, bool Created);

public sealed class SubmitListingReportCommandValidator : AbstractValidator<SubmitListingReportCommand>
{
    public SubmitListingReportCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty().WithMessage("حدد الإعلان المبلّغ عنه.");
        RuleFor(x => x.Reason).IsInEnum().WithMessage("اختر سبب البلاغ.").WithErrorCode("REPORT_REASON_INVALID");
        RuleFor(x => x.Details)
            .MaximumLength(ListingReportPolicy.MaxDetailsLength)
            .WithErrorCode("REPORT_DETAILS_TOO_LONG");
        RuleFor(x => x.Details)
            .Must(d => !string.IsNullOrWhiteSpace(d) && d.Trim().Length >= ListingReportPolicy.MinDetailsLengthWhenRequired)
            .When(x => Enum.IsDefined(x.Reason) && ListingReportPolicy.RequiresDetails(x.Reason))
            .WithMessage("اشرح المشكلة في حقل التفاصيل.")
            .WithErrorCode("REPORT_DETAILS_REQUIRED");
    }
}

/// <summary>
/// Saves a report. Under an advisory lock on the reporter (so a double submit cannot slip two
/// reports past the checks): the listing must be one a visitor can see now, the reporter must not
/// own it, have an open report on it, or have had the same reason reviewed on it recently, and must
/// be under the daily limit. The listing and its owner come from the database, never from the client.
/// Admins are told after the commit; a failing notification never loses the report.
/// </summary>
public sealed class SubmitListingReportCommandHandler : IRequestHandler<SubmitListingReportCommand, SubmitListingReportResult>
{
    private readonly IListingReportRepository _reports;
    private readonly IUnitOfWork _uow;
    private readonly ListingReportNotifier _notifier;
    private readonly ListingReportOptions _options;
    private readonly ILogger<SubmitListingReportCommandHandler> _logger;

    public SubmitListingReportCommandHandler(
        IListingReportRepository reports,
        IUnitOfWork uow,
        ListingReportNotifier notifier,
        IOptions<ListingReportOptions> options,
        ILogger<SubmitListingReportCommandHandler> logger)
    {
        _reports = reports;
        _uow = uow;
        _notifier = notifier;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SubmitListingReportResult> Handle(SubmitListingReportCommand request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        ListingReport report;
        bool isFirstOpenOnListing;

        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireAdvisoryLockAsync(ListingReportLocks.ForReporter(request.ReporterUserId), ct);

            if (request.ClientRequestId is { } clientRequestId && clientRequestId != Guid.Empty)
            {
                var replay = await _reports.FindByClientRequestAsync(request.ReporterUserId, clientRequestId, ct);
                if (replay is not null)
                {
                    await _uow.CommitTransactionAsync(ct);
                    return new(await ListingReportViews.ForReporterAsync(replay, _reports, ct), Created: false);
                }
            }

            var listing = await _reports.GetReportableListingAsync(request.PropertyId, now, ct)
                ?? throw new NotFoundException("الإعلان غير موجود أو لم يعد منشورًا.");

            if (listing.OwnerId == request.ReporterUserId)
                throw new ConflictException("لا يمكنك الإبلاغ عن إعلانك.", ListingReportErrorCodes.OwnListing);

            var open = await _reports.FindOpenAsync(request.ReporterUserId, listing.Id, ct);
            if (open is not null)
                throw new ConflictException(
                    $"لديك بلاغ مفتوح عن هذا الإعلان ({open.ReferenceNumber}) وهو قيد المعالجة.",
                    ListingReportErrorCodes.AlreadyOpen);

            var lastClosed = await _reports.LastClosedAtAsync(request.ReporterUserId, listing.Id, request.Reason, ct);
            if (lastClosed is { } closedAt && closedAt > now.AddDays(-Math.Max(0, _options.SameReasonCooldownDays)))
                throw new ConflictException(
                    "راجعت الإدارة بلاغك السابق عن هذا الإعلان للسبب نفسه مؤخرًا. اختر سببًا آخر إذا ظهرت مشكلة جديدة.",
                    ListingReportErrorCodes.RecentlyReviewed);

            var sentToday = await _reports.CountByReporterSinceAsync(request.ReporterUserId, now.AddHours(-24), ct);
            if (sentToday >= Math.Max(1, _options.DailyLimitPerReporter))
            {
                _logger.LogWarning(
                    "Listing report daily limit reached. ReporterUserId={ReporterUserId}, Count={Count}",
                    request.ReporterUserId, sentToday);
                throw new TooManyRequestsException(
                    "وصلت إلى الحد اليومي لعدد البلاغات. حاول لاحقًا.",
                    ListingReportErrorCodes.DailyLimit,
                    TimeSpan.FromHours(1));
            }

            report = ListingReport.Submit(
                request.ReporterUserId, listing.Id, listing.OwnerId, listing.Title,
                request.Reason, request.Details, request.ClientRequestId, now);

            await _reports.AddAsync(report, ct);
            await _reports.AddEventAsync(ListingReportEvent.Record(
                report.Id, request.ReporterUserId, ListingReportEventType.Created, isInternal: false, now,
                toValue: report.Reason.ToString()), ct);

            var othersOpen = await _reports.ListOpenForPropertyAsync(listing.Id, ct);
            isFirstOpenOnListing = othersOpen.Count == 0;
            await EscalateIfManyReportersAsync(report, othersOpen, now, ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await _uow.RollbackTransactionAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "Listing report submitted. ReportId={ReportId}, PropertyId={PropertyId}, Reason={Reason}, Priority={Priority}",
            report.Id, report.PropertyId, report.Reason, report.Priority);

        // Tell the admins about the first open report on a listing and about every high-priority one;
        // further routine reports on a listing already in the queue only update the queue.
        if (isFirstOpenOnListing || report.Priority >= ListingReportPriority.High)
            await _notifier.NotifyAdminsOfNewReportAsync(report, ct);

        return new(await ListingReportViews.ForReporterAsync(report, _reports, ct), Created: true);
    }

    /// <summary>
    /// Several different accounts reporting the same listing is a reason to look sooner, so every open
    /// report on it is raised to at least High. It is never a reason to hide anything.
    /// </summary>
    private async Task EscalateIfManyReportersAsync(
        ListingReport report, IReadOnlyList<ListingReport> othersOpen, DateTime now, CancellationToken ct)
    {
        var distinct = othersOpen.Select(r => r.ReporterUserId).Append(report.ReporterUserId).Distinct().Count();
        if (distinct < ListingReportPolicy.DistinctReportersForEscalation)
            return;

        foreach (var candidate in othersOpen.Append(report))
        {
            var before = candidate.Priority;
            if (candidate.RaisePriorityTo(ListingReportPriority.High, now))
            {
                await _reports.AddEventAsync(ListingReportEvent.Record(
                    candidate.Id, actorUserId: null, ListingReportEventType.PriorityChanged, isInternal: true, now,
                    note: $"distinct-open-reporters={distinct}", fromValue: before.ToString(), toValue: candidate.Priority.ToString()), ct);
            }
        }
    }
}

// ── The reporter's own reports ───────────────────────────────────────────

public sealed record GetMyListingReportsQuery(Guid ReporterUserId, int Page, int PageSize)
    : IRequest<ListingReportPageDto<MyListingReportDto>>;

public sealed class GetMyListingReportsQueryHandler
    : IRequestHandler<GetMyListingReportsQuery, ListingReportPageDto<MyListingReportDto>>
{
    private readonly IListingReportRepository _reports;
    public GetMyListingReportsQueryHandler(IListingReportRepository reports) => _reports = reports;

    public async Task<ListingReportPageDto<MyListingReportDto>> Handle(GetMyListingReportsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);
        var (items, total) = await _reports.ListByReporterAsync(request.ReporterUserId, page, size, ct);
        var dtos = new List<MyListingReportDto>(items.Count);
        foreach (var report in items)
            dtos.Add(await ListingReportViews.ForReporterAsync(report, _reports, ct));
        return new(dtos, total, page, size);
    }
}

public sealed record GetMyListingReportQuery(Guid ReporterUserId, Guid ReportId) : IRequest<MyListingReportDto>;

public sealed class GetMyListingReportQueryHandler : IRequestHandler<GetMyListingReportQuery, MyListingReportDto>
{
    private readonly IListingReportRepository _reports;
    public GetMyListingReportQueryHandler(IListingReportRepository reports) => _reports = reports;

    public async Task<MyListingReportDto> Handle(GetMyListingReportQuery request, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(request.ReportId, ct);
        if (report is null || report.ReporterUserId != request.ReporterUserId)
            throw new NotFoundException("البلاغ غير موجود.");

        return await ListingReportViews.ForReporterAsync(report, _reports, ct);
    }
}

public sealed record GetMyOpenReportForListingQuery(Guid ReporterUserId, Guid PropertyId) : IRequest<MyOpenReportStatusDto>;

public sealed class GetMyOpenReportForListingQueryHandler : IRequestHandler<GetMyOpenReportForListingQuery, MyOpenReportStatusDto>
{
    private readonly IListingReportRepository _reports;
    public GetMyOpenReportForListingQueryHandler(IListingReportRepository reports) => _reports = reports;

    public async Task<MyOpenReportStatusDto> Handle(GetMyOpenReportForListingQuery request, CancellationToken ct)
    {
        var open = await _reports.FindOpenAsync(request.ReporterUserId, request.PropertyId, ct);
        return open is null
            ? new(false, null, null, null)
            : new(true, open.Id, open.ReferenceNumber, ListingReportViews.ReporterStatus(open.Status));
    }
}

// ── The reporter answers an info request ─────────────────────────────────

public sealed record RespondToListingReportCommand(Guid ReporterUserId, Guid ReportId, string Message) : IRequest<MyListingReportDto>;

public sealed class RespondToListingReportCommandValidator : AbstractValidator<RespondToListingReportCommand>
{
    public RespondToListingReportCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().WithMessage("اكتب المعلومات المطلوبة.")
            .MaximumLength(ListingReportPolicy.MaxDetailsLength);
    }
}

/// <summary>
/// Adds the reporter's answer next to the original report (which stays unchanged) and sends the
/// report back to review. Allowed only while an admin is waiting for that answer.
/// </summary>
public sealed class RespondToListingReportCommandHandler : IRequestHandler<RespondToListingReportCommand, MyListingReportDto>
{
    private readonly IListingReportRepository _reports;
    private readonly IUnitOfWork _uow;
    private readonly ListingReportNotifier _notifier;

    public RespondToListingReportCommandHandler(IListingReportRepository reports, IUnitOfWork uow, ListingReportNotifier notifier)
    {
        _reports = reports;
        _uow = uow;
        _notifier = notifier;
    }

    public async Task<MyListingReportDto> Handle(RespondToListingReportCommand request, CancellationToken ct)
    {
        var report = await _reports.GetByIdAsync(request.ReportId, ct);
        if (report is null || report.ReporterUserId != request.ReporterUserId)
            throw new NotFoundException("البلاغ غير موجود.");

        var now = DateTime.UtcNow;
        var before = report.Status;
        report.RecordReporterResponse(request.ReporterUserId, now);
        var responded = ListingReportEvent.Record(
            report.Id, request.ReporterUserId, ListingReportEventType.ReporterResponded, isInternal: false, now,
            note: request.Message, fromValue: before.ToString(), toValue: report.Status.ToString());
        await _reports.AddEventAsync(responded, ct);
        await _uow.SaveChangesAsync(ct);

        // Without this the answer sat in the timeline and no admin ever learned it had arrived.
        await _notifier.NotifyAdminsOfReporterResponseAsync(report, responded.Id, request.Message, ct);

        return await ListingReportViews.ForReporterAsync(report, _reports, ct);
    }
}

internal static class ListingReportLocks
{
    /// <summary>Stable advisory-lock key for one reporter's submissions.</summary>
    public static long ForReporter(Guid reporterUserId) =>
        BitConverter.ToInt64(reporterUserId.ToByteArray(), 0) ^ 0x4C52_5052_5054_5231;

    /// <summary>Stable advisory-lock key for one report's admin decisions.</summary>
    public static long ForReport(Guid reportId) =>
        BitConverter.ToInt64(reportId.ToByteArray(), 8) ^ 0x4C52_4144_4D49_4E31;
}
