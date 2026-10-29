using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Moderation;
using HudhudNestApi.Domain.Moderation.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// "إدارة البلاغات": the admin queue of listing reports and every decision on them. Admin-only on
/// the class, so each action is authorized on the server; the acting admin always comes from the
/// token. Request bodies carry only the decision's own inputs (no status, no actor), so a client
/// cannot set fields it was not asked for.
/// </summary>
[ApiController]
[Route("api/admin/listing-reports")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminListingReportsController : ControllerBase
{
    private readonly ISender _mediator;

    public AdminListingReportsController(ISender mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(ListingReportPageDto<AdminListingReportSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] ReportQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(new SearchListingReportsQuery(query.ToFilter()), ct));

    /// <summary>Counts for the summary cards, under the same date/reason/priority/assignee/listing/search filters.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(AdminListingReportStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats([FromQuery] ReportQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(new GetListingReportStatsQuery(query.ToFilter()), ct));

    /// <summary>Admins a report can be assigned to.</summary>
    [HttpGet("assignees")]
    [ProducesResponseType(typeof(IReadOnlyList<UserLabel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Assignees(CancellationToken ct)
        => Ok(await _mediator.Send(new ListListingReportAssigneesQuery(), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminListingReportDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetListingReportForAdminQuery(id), ct));

    [HttpPost("{id:guid}/start-review")]
    public Task<IActionResult> StartReview(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] OptionalNoteBody? body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.StartReview, body?.Note, ct: ct);

    [HttpPost("{id:guid}/assign")]
    public Task<IActionResult> Assign(Guid id, [FromBody] AssignBody body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.Assign, null, assignee: body.AdminUserId, ct: ct);

    [HttpPost("{id:guid}/priority")]
    public Task<IActionResult> Priority(Guid id, [FromBody] PriorityBody body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.ChangePriority, body.Reason, priority: body.Priority, ct: ct);

    [HttpPost("{id:guid}/notes")]
    public Task<IActionResult> AddNote(Guid id, [FromBody] NoteBody body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.AddNote, body.Note, ct: ct);

    /// <summary>Asks the reporter a question. The message is shown to the reporter; write nothing internal in it.</summary>
    [HttpPost("{id:guid}/request-info")]
    public Task<IActionResult> RequestInfo(Guid id, [FromBody] MessageBody body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.RequestInfo, body.Message, ct: ct);

    [HttpPost("{id:guid}/action-required")]
    public Task<IActionResult> ActionRequired(Guid id, [FromBody] NoteBody body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.MarkActionRequired, body.Note, ct: ct);

    /// <summary>Closes the report with a recorded decision. The outcome decides Resolved or Dismissed.</summary>
    [HttpPost("{id:guid}/close")]
    public Task<IActionResult> Close(Guid id, [FromBody] CloseBody body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.Close, body.Note, outcome: body.Outcome, ct: ct);

    [HttpPost("{id:guid}/reopen")]
    public Task<IActionResult> Reopen(Guid id, [FromBody] ReasonBody body, CancellationToken ct)
        => Act(id, ListingReportAdminAction.Reopen, body.Reason, ct: ct);

    /// <summary>Takes the listing down with the existing take-down (reversible). The reason is shown to the owner.</summary>
    [HttpPost("{id:guid}/take-down-listing")]
    public async Task<IActionResult> TakeDownListing(Guid id, [FromBody] ReasonBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new ListingReportListingActionCommand(AdminId(), id, TakeDown: true, body.Reason, ClientIp()), ct));

    [HttpPost("{id:guid}/restore-listing")]
    public async Task<IActionResult> RestoreListing(Guid id, [FromBody] ReasonBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new ListingReportListingActionCommand(AdminId(), id, TakeDown: false, body.Reason, ClientIp()), ct));

    private async Task<IActionResult> Act(
        Guid id,
        ListingReportAdminAction action,
        string? text,
        Guid? assignee = null,
        ListingReportPriority? priority = null,
        ListingReportOutcome? outcome = null,
        CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListingReportAdminCommand(
            AdminId(), id, action, text, assignee, priority, outcome, ClientIp()), ct));

    private Guid AdminId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id)
            ? id
            : throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    public sealed class ReportQuery
    {
        public string? Search { get; init; }
        public ListingReportStatus? Status { get; init; }
        /// <summary>Only New, UnderReview, WaitingForInformation and ActionRequired (ignored when Status is set).</summary>
        public bool Open { get; init; }
        public ListingReportReason? Reason { get; init; }
        public ListingReportPriority? Priority { get; init; }
        public Guid? AssignedAdminId { get; init; }
        public bool Unassigned { get; init; }
        public Guid? PropertyId { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public bool Overdue { get; init; }
        public ListingReportSort Sort { get; init; } = ListingReportSort.Newest;
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;

        public ListingReportFilter ToFilter() => new(
            Search, Status, Open, Reason, Priority, AssignedAdminId, Unassigned, PropertyId,
            AsUtc(From),
            AsUtc(To),
            Overdue, Sort, Page, PageSize);

        private static DateTime? AsUtc(DateTime? value) => value switch
        {
            null => null,
            { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
            { } other => DateTime.SpecifyKind(other, DateTimeKind.Utc),
        };
    }

    public sealed record OptionalNoteBody(string? Note);
    public sealed record NoteBody(string Note);
    public sealed record MessageBody(string Message);
    public sealed record ReasonBody(string Reason);
    public sealed record AssignBody(Guid? AdminUserId);
    public sealed record PriorityBody(ListingReportPriority Priority, string? Reason);
    public sealed record CloseBody(ListingReportOutcome Outcome, string Note);
}
