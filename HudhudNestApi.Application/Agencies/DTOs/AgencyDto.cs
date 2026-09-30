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
    int? NeighborhoodId = null);

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
