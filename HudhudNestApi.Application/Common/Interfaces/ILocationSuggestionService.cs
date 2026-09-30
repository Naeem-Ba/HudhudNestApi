using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Domain.Lookups.Entities;

namespace HudhudNestApi.Application.Common.Interfaces;

/// <summary>
/// Handles user-submitted district/neighborhood names that aren't in the
/// seeded catalog — see LocationSuggestion's doc comment for the full
/// reasoning (deliberately-incomplete seed data + a moderation queue beats
/// either guessing at unverifiable names or leaving users stuck).
/// </summary>
public interface ILocationSuggestionService
{
    /// <summary>
    /// Submits a district-name suggestion under the given governorate.
    /// No-op (returns without creating a row) if a Pending suggestion with
    /// the same normalized name already exists for this governorate, so
    /// repeated listings using the same manual name don't spam the queue.
    /// </summary>
    Task SubmitDistrictSuggestionAsync(
        int governorateId,
        string name,
        Guid submittedByUserId,
        Guid? propertyId,
        CancellationToken ct = default);

    /// <summary>Same as above, for a neighborhood name under a district.</summary>
    Task SubmitNeighborhoodSuggestionAsync(
        int districtId,
        string name,
        Guid submittedByUserId,
        Guid? propertyId,
        CancellationToken ct = default);

    Task<IReadOnlyList<LocationSuggestionDto>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Approves a suggestion: creates the real District/Neighborhood row from
    /// the submitted name, marks the suggestion Approved, and best-effort
    /// backfills any Properties whose DistrictText/NeighborhoodText matches
    /// (case/whitespace-insensitive, same parent) to reference the new row
    /// instead — those listings "graduate" out of manual text automatically.
    /// Throws if the suggestion doesn't exist or isn't Pending.
    /// </summary>
    Task<int> ApproveAsync(Guid suggestionId, Guid reviewedByUserId, string? notes, CancellationToken ct = default);

    /// <summary>Throws if the suggestion doesn't exist or isn't Pending.</summary>
    Task RejectAsync(Guid suggestionId, Guid reviewedByUserId, string? notes, CancellationToken ct = default);
}
