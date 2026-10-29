namespace HudhudNestApi.Domain.Moderation.Enums;

/// <summary>What one timeline row of a report records. Stored by name.</summary>
public enum ListingReportEventType
{
    Created = 1,
    StatusChanged = 2,
    PriorityChanged = 3,
    Assigned = 4,
    Unassigned = 5,
    NoteAdded = 6,
    InfoRequested = 7,
    ReporterResponded = 8,
    ListingTakenDown = 9,
    ListingRestored = 10,
    Closed = 11,
    Reopened = 12,
}
