namespace HudhudNestApi.Domain.Search.Enums;

/// <summary>How often a saved search may notify its owner (growth engine P3).</summary>
public enum SavedSearchFrequency
{
    /// <summary>One notification per new listing, checked every sweep (the behavior before P3).</summary>
    Instant = 1,

    /// <summary>At most one summary notification per 24 hours with the number of new listings.</summary>
    Daily = 2,
}
