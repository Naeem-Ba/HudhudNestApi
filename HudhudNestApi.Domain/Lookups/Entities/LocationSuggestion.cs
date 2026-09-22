using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Domain.Lookups.Entities;

public enum LocationSuggestionType
{
    /// <summary>ParentId is a GovernorateId.</summary>
    District = 1,

    /// <summary>ParentId is a DistrictId.</summary>
    Neighborhood = 2
}

public enum LocationSuggestionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// A user-typed district/neighborhood name that isn't in the seeded catalog
/// yet (see NeighborhoodsSeed.cs — most of Syria has no seeded neighborhoods,
/// and a handful of small localities may not be in the district seed either).
/// Submitted automatically when a listing is created/updated with
/// Property.DistrictText or Property.NeighborhoodText set instead of the
/// corresponding *Id, and reviewed by an admin (see
/// LocationSuggestionsController) before it becomes a real, selectable
/// District/Neighborhood row — this is the deliberate alternative to either
/// (a) guessing at exhaustive location data nobody can verify, or (b) leaving
/// users in uncovered areas permanently stuck typing free text with no path
/// to a proper dropdown entry.
/// </summary>
public class LocationSuggestion : BaseEntity
{
    public LocationSuggestionType Type { get; private set; }
    public int ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid SubmittedByUserId { get; private set; }
    public Guid? PropertyId { get; private set; }
    public LocationSuggestionStatus Status { get; private set; } = LocationSuggestionStatus.Pending;
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public string? ReviewNotes { get; private set; }

    /// <summary>The real District/Neighborhood row's Id once approved.</summary>
    public int? ResultingEntityId { get; private set; }

    private LocationSuggestion() { }

    public static LocationSuggestion Create(
        LocationSuggestionType type,
        int parentId,
        string name,
        Guid submittedByUserId,
        Guid? propertyId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Suggested location name is required.");

        if (parentId <= 0)
            throw new DomainException("A valid parent (governorate/district) is required.");

        return new LocationSuggestion
        {
            Type = type,
            ParentId = parentId,
            Name = name.Trim(),
            SubmittedByUserId = submittedByUserId,
            PropertyId = propertyId,
            Status = LocationSuggestionStatus.Pending
        };
    }

    public void Approve(Guid reviewedByUserId, int resultingEntityId, string? notes = null)
    {
        if (Status != LocationSuggestionStatus.Pending)
            throw new DomainException("Only pending suggestions can be approved.");

        Status = LocationSuggestionStatus.Approved;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAt = DateTime.UtcNow;
        ReviewNotes = notes;
        ResultingEntityId = resultingEntityId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(Guid reviewedByUserId, string? notes = null)
    {
        if (Status != LocationSuggestionStatus.Pending)
            throw new DomainException("Only pending suggestions can be rejected.");

        Status = LocationSuggestionStatus.Rejected;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAt = DateTime.UtcNow;
        ReviewNotes = notes;
        UpdatedAt = DateTime.UtcNow;
    }
}
