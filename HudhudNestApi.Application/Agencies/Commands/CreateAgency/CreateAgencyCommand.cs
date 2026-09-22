using MediatR;
using HudhudNestApi.Application.Agencies.DTOs;

namespace HudhudNestApi.Application.Agencies.Commands.CreateAgency;

/// <summary>
/// Registers a real-estate office and makes the caller its owner.
/// </summary>
public sealed record CreateAgencyCommand(
    string Name,
    string Slug,
    string CountryCode,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? City,

    /// <summary>
    /// Optional licence number, typed by a human. Never inferred, never extracted from an
    /// uploaded document — the architecture review is explicit that legal identifiers must
    /// come from a person and nothing else.
    /// </summary>
    string? LicenseNumber,

    Guid RequestingUserId,
    string? IpAddress,

    /// <summary>
    /// المحافظة (FK → Governorates) — اختيارية، مثل Property.GovernorateId، حفاظًا على
    /// إمكانية إنشاء مكتب بالطريقة القديمة (بلا موقع منظَّم) أثناء فترة التوافق.
    /// إن أُرسلت DistrictId فيجب أن تُرسل هذه أيضًا — انظر CreateAgencyCommandValidator.
    /// </summary>
    int? GovernorateId = null,

    /// <summary>أدق مستوى بعد المحافظة. يجب أن ينتمي فعليًا إلى GovernorateId.</summary>
    int? DistrictId = null,

    /// <summary>أدق مستوى. يجب أن ينتمي فعليًا إلى DistrictId.</summary>
    int? NeighborhoodId = null) : IRequest<AgencyDto>;
