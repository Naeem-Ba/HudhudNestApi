namespace HudhudNestApi.Application.Admin.DTOs;

/// <summary>
/// One listing row on the admin "user's ads" screen — deliberately not the full
/// Properties.DTOs.PropertyDto: this is an admin back-office list, not the public listing
/// shape, and needs the lifecycle fields the admin acts on, nothing else.
/// </summary>
public sealed class AdminPropertySummaryDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public bool IsFeatured { get; init; }
    public DateTime? FeaturedUntil { get; init; }
    public DateTime CreatedAt { get; init; }
}
