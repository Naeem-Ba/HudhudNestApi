// ═══════════════════════════════════════════════════════════════
// ✅ إصلاح M-1 (الجزء 1 من 3): إضافة HasCompletedVisitAsync لـ IVisitRepository
//
// المشكلة القديمة:
//   لم يكن هناك أي طريقة للتحقق من زيارة مكتملة.
//   أي مستخدم يستطيع تقييم أي عقار بدون أن يكون قد زاره فعلاً.
//
// الإصلاح:
//   إضافة HasCompletedVisitAsync للـ Interface ليستطيع AddReviewCommandHandler
//   التحقق من وجود زيارة مكتملة قبل السماح بكتابة التقييم.
//
// التعليم — Interface Segregation (مبدأ SOLID):
//   نضيف الـ method للـ Interface الموجود لأنها تنتمي لنفس المسؤولية (Visit queries).
//   لو كانت مسؤولية مختلفة تماماً، لأنشأنا Interface منفصلاً.
// ═══════════════════════════════════════════════════════════════
using PropertyApi.Domain.Bookings.Entities;

namespace PropertyApi.Application.Bookings.Interfaces;

public interface IVisitRepository
{
    // ── Write operations ───────────────────────────────────────
    Task AddAsync(VisitRequest visit, CancellationToken ct = default);

    // ── Read operations ────────────────────────────────────────
    Task<VisitRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> HasPendingVisitAsync(
        Guid propertyId,
        Guid requesterId,
        CancellationToken ct = default);

    Task<IReadOnlyList<VisitRequest>> GetByRequesterIdAsync(
        Guid requesterId,
        CancellationToken ct = default);

    Task<IReadOnlyList<VisitRequest>> GetByPropertyIdAsync(
        Guid propertyId,
        CancellationToken ct = default);

    // ✅ جديد — مطلوب لـ Review Eligibility Validation
    /// <summary>
    /// يتحقق من أن المستخدم أتمَّ زيارة لهذا العقار (Status = Completed).
    /// يُستخدم في AddReviewCommandHandler للتأكد من أن المُقيِّم زار العقار فعلاً.
    /// </summary>
    /// <param name="propertyId">معرف العقار المراد تقييمه.</param>
    /// <param name="userId">معرف المستخدم الذي يريد كتابة التقييم.</param>
    /// <param name="ct">CancellationToken للإلغاء عند الحاجة.</param>
    /// <returns>true إذا وُجدت زيارة مكتملة، false خلاف ذلك.</returns>
    Task<bool> HasCompletedVisitAsync(
        Guid propertyId,
        Guid userId,
        CancellationToken ct = default);
}