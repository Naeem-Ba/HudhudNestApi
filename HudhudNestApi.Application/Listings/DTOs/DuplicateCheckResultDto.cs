namespace HudhudNestApi.Application.Listings.DTOs;

/// <summary>
/// Result of an advisory (non-blocking) potential-duplicate-listing check.
/// This is intentionally never surfaced through FluentValidation/ValidationBehavior:
/// that pipeline hard-fails the whole request on any error (see ValidationBehavior),
/// so a "maybe duplicate" signal must stay a separate opt-in read, not a blocking rule.
/// The frontend calls this before final submit and shows a dismissible confirm dialog.
/// </summary>
public sealed class DuplicateCheckResultDto
{
    public bool HasPotentialDuplicates { get; set; }
    public List<DuplicateCandidateDto> Candidates { get; set; } = new();
}

public sealed class DuplicateCandidateDto
{
    public Guid PropertyId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>True when the candidate is owned by the same user submitting the check (a likely repost).</summary>
    public bool IsSameOwner { get; set; }

    /// <summary>Human-readable reason shown to the user, e.g. "Same neighborhood, area within 5%".</summary>
    public string MatchReason { get; set; } = string.Empty;
}
