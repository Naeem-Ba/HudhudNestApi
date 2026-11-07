namespace HudhudNestApi.Application.Agencies.DTOs;

/// <summary>
/// An agency as shown on its public page and in the owner's dashboard.
/// </summary>
public sealed record AgencyDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? ContactEmail,
    string? ContactPhone,
    string? City,
    string CountryCode,

    /// <summary>
    /// Licence number exactly as the agency typed it. Never validated or verified by this
    /// system — a value being present here says only that someone entered it, and clients
    /// must not present it as accreditation.
    /// </summary>
    string? LicenseNumber,

    Guid OwnerUserId,
    bool IsActive,
    int MemberCount,
    DateTime CreatedAt,
    IReadOnlyList<AgencyMemberDto> Members,

    /// <summary>
    /// المحافظة/المنطقة/الحي المنظَّمون — نفس حقول Property.GovernorateId/DistrictId/
    /// NeighborhoodId. جميعها null لأي مكتب لم يُصنَّف جغرافيًا بعد (مكاتب قديمة أو مكتب
    /// جديد لم يحدد موقعه) — "غير مصنَّف"، وليس خطأ.
    /// </summary>
    int? GovernorateId = null,
    int? DistrictId = null,
    int? NeighborhoodId = null,

    /// <summary>
    /// The owner's plan as it is right now ("NoPlan", "Active", "Expired" or "Cancelled") and when it
    /// ends. Filled only for the caller's own office (GET /api/agencies/me), never on the public
    /// page. It lets the client keep the office visible, with a renewal notice, for the grace period
    /// after the owner's plan ends, for the owner and for every member alike.
    /// </summary>
    string? OwnerPlanStatus = null,
    DateTime? OwnerPlanExpiresAt = null);

/// <summary>
/// A member of an agency. Deliberately thin: name, avatar, and whether they own the
/// agency. Contact details belong to the member's own profile, not to the agency page.
/// </summary>
public sealed record AgencyMemberDto(
    Guid UserId,
    string DisplayName,
    string? ProfileImageUrl,
    bool IsOwner,
    DateTime? JoinedAt);
