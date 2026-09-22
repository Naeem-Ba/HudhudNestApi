namespace HudhudNestApi.Application.Listings.DTOs;

/// <summary>One aggregate row of "how many of this event happened, grouped by attributed UtmSource" — the building block of the Phase 14 Social Performance report. <see cref="UtmSource"/> is null for events with no UTM (direct/organic traffic).</summary>
public sealed record UtmSourceCount(string? UtmSource, int Count);
