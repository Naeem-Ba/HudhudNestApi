namespace PropertyApi.Domain.Marketing.Enums;

/// <summary>Admin-only follow-up pipeline for a Lead. Never set by the public submission
/// endpoint — a new Lead always starts at <see cref="New"/>.</summary>
public enum LeadStatus
{
    New = 1,
    Contacted = 2,
    Qualified = 3,
    Converted = 4,
    Rejected = 5
}
