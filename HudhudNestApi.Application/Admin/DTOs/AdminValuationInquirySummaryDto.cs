namespace HudhudNestApi.Application.Admin.DTOs;

/// <summary>
/// One row on the Stage 8 Admin Dashboard's inquiry list. Mirrors AdminPropertySummaryDto's
/// shape (a back-office list needs the lifecycle fields the admin is monitoring, not the full
/// entity) — status is exposed as the real ValuationInquiryStatus enum's name, matching
/// AdminPropertySummaryDto.Status's own "ToString() the real enum" convention. Deliberately
/// carries no customer contact information: ValuationInquiry itself stores none yet (that is
/// Stage 9's consent-gated addition) — RequesterId is the only identity field, same as every
/// other reader of this entity.
/// </summary>
public sealed class AdminValuationInquirySummaryDto
{
    public Guid Id { get; init; }
    public Guid? RequesterId { get; init; }
    public string Status { get; init; } = string.Empty;
    public int GovernorateId { get; init; }
    public int? DistrictId { get; init; }
    public int? NeighborhoodId { get; init; }
    public int? PropertyTypeId { get; init; }
    public decimal? Area { get; init; }
    public int? Rooms { get; init; }
    public string RequestType { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}
