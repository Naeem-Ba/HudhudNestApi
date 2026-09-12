using MediatR;
using PropertyApi.Application.Agencies.DTOs;

namespace PropertyApi.Application.Agencies.Commands.UpdateAgency;

/// <summary>
/// Owner-only update of the agency's editable profile fields — exactly the fields
/// Agency.UpdateProfile has always accepted. RELEASE-BLOCKERS-AR.md B-4: the domain method
/// was already complete; this is the thin command layer the report says was missing.
/// </summary>
public sealed record UpdateAgencyCommand(
    Guid AgencyId,
    string Name,
    string? Description,
    string? ContactEmail,
    string? ContactPhone,
    string? City,
    Guid RequestingUserId,

    /// <summary>
    /// المحافظة الجديدة، أو null لإلغاء تصنيف المكتب جغرافيًا. مثل بقية حقول هذا الأمر
    /// (Description/ContactEmail/...)، الاستبدال هنا كامل لا جزئي: القيمة المرسلة —
    /// بما فيها null — تحل محل القيمة الحالية دائمًا، فعلى الواجهة إرسال الموقع الحالي
    /// كاملاً حتى عند تعديل حقل آخر فقط. هذا ما يسمح فعليًا بإزالة District/Neighborhood
    /// (البند 7) بإرسال null بدل تركه بلا تغيير.
    /// </summary>
    int? GovernorateId = null,
    int? DistrictId = null,
    int? NeighborhoodId = null) : IRequest<AgencyDto>;
