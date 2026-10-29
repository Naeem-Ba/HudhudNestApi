namespace HudhudNestApi.Domain.Moderation.Enums;

/// <summary>Review order. Stored as a number so "highest first" sorts in the database.</summary>
public enum ListingReportPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}
