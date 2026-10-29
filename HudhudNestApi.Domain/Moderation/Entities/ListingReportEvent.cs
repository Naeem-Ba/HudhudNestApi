using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Domain.Moderation.Entities;

/// <summary>
/// Append-only timeline row of a report: who did what, when, and what changed. There is no update
/// or delete path for these rows in the application, and no endpoint lets a reporter or an owner
/// write one directly; the actor always comes from the signed-in account on the server.
///
/// <see cref="IsInternal"/> rows (admin notes, assignment, priority, listing actions, the closing
/// note) are admin-only. The reporter's own view filters them out on the server, never in the client.
/// </summary>
public sealed class ListingReportEvent : BaseEntity
{
    public const int MaxValueLength = 60;

    private ListingReportEvent() { }

    public Guid ReportId { get; private set; }

    /// <summary>Who did it. Null only for a system action.</summary>
    public Guid? ActorUserId { get; private set; }

    public ListingReportEventType Type { get; private set; }

    /// <summary>The value before the change (a status, a priority, an admin id), when there is one.</summary>
    public string? FromValue { get; private set; }

    /// <summary>The value after the change.</summary>
    public string? ToValue { get; private set; }

    /// <summary>The text that goes with the event (a note, a question, a reason). Plain text, never markup.</summary>
    public string? Note { get; private set; }

    public bool IsInternal { get; private set; }

    public static ListingReportEvent Record(
        Guid reportId,
        Guid? actorUserId,
        ListingReportEventType type,
        bool isInternal,
        DateTime utcNow,
        string? note = null,
        string? fromValue = null,
        string? toValue = null)
    {
        if (reportId == Guid.Empty)
            throw new ArgumentException("An event needs a report.", nameof(reportId));

        return new ListingReportEvent
        {
            ReportId = reportId,
            ActorUserId = actorUserId,
            Type = type,
            IsInternal = isInternal,
            Note = Clip(note, ListingReportPolicy.MaxNoteLength),
            FromValue = Clip(fromValue, MaxValueLength),
            ToValue = Clip(toValue, MaxValueLength),
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
