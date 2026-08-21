using MediatR;
using PropertyApi.Application.Reviews.DTOs;

namespace PropertyApi.Application.Reviews.Queries.GetRatingEligibility;

/// <summary>
/// يفحص ما إذا كان بإمكان <paramref name="RaterId"/> تقييم
/// <paramref name="RatedUserId"/> الآن — نفس شرط الأهلية المُطبَّق داخل
/// RateUserCommandHandler، لكن كـ Query للقراءة فقط تستدعيه الواجهة قبل
/// عرض نموذج التقييم (بدل أن يكتشف المستخدم الرفض بعد الإرسال).
/// </summary>
public sealed record GetRatingEligibilityQuery(
    Guid RatedUserId,
    Guid RaterId
) : IRequest<RatingEligibilityDto>;
