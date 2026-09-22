namespace HudhudNestApi.Application.Users.DTOs;

/// <summary>
/// Public profile page data for one user (the "seller/agent profile" page).
/// Deliberately excludes Email and only exposes PhoneNumber when the user
/// has the Agent role — same privacy rule GetUserByIdQueryHandler already
/// applies to UserSummaryDto, kept consistent here.
///
/// Active listings are NOT embedded here — the frontend fetches those from
/// the existing public GET /api/Properties?ownerId=... endpoint (already
/// published-only, already paginated), avoiding a second, drifting copy of
/// listing-shape logic on this DTO.
/// </summary>
public sealed class UserProfileDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public DateTime MemberSince { get; set; }
    public bool IsAgent { get; set; }

    /// <summary>نبذة تعريفية اختيارية يكتبها صاحب الحساب عن نفسه/عمله.</summary>
    public string? Bio { get; set; }

    /// <summary>عنوان/معلومات تواصل حرة اختيارية يختار صاحب الحساب عرضها علنًا.</summary>
    public string? ContactInfo { get; set; }

    public int SoldCount { get; set; }
    public int RentedCount { get; set; }

    public double AverageCredibility { get; set; }
    public double AverageSafety { get; set; }
    public double AverageResponseSpeed { get; set; }
    public double AverageTransparency { get; set; }
    public double AverageOverall { get; set; }
    public int RatingsCount { get; set; }
}
