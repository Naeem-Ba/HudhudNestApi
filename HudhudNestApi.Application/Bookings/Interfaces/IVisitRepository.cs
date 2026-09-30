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
using HudhudNestApi.Domain.Bookings.Entities;

namespace HudhudNestApi.Application.Bookings.Interfaces;

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

    // ✅ إصلاح: كانت GetMyVisitsQueryHandler تستدعي GetByRequesterIdAsync
    // فقط — أي أن "زياراتي" كانت تعني حصريًا "الزيارات التي طلبتُها كزائر"،
    // ولا تتضمن أبدًا "طلبات الزيارة التي وصلتني كمالك عقار". هذا كان السبب
    // الحقيقي وراء عدم وصول طلبات الزيارة لحساب البائع: الطلب كان يُحفَظ
    // بنجاح بقاعدة البيانات، لكن استعلام /Visits/mine لم يكن يجلبه إطلاقًا
    // لأن المالك ليس هو مُقدِّم الطلب (RequesterId). هذه الدالة الجديدة تجمع
    // الاتجاهين معًا في استعلام واحد.
    Task<IReadOnlyList<VisitRequest>> GetByRequesterOrOwnerIdAsync(
        Guid userId,
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

    // ✅ جديد — مطلوب لشرط "زيارة مكتملة أو مراسلة" قبل تقييم مستخدم آخر.
    /// <summary>
    /// يتحقق مما إذا كان <paramref name="raterId"/> قد أتمَّ زيارة (Status = Completed)
    /// لأي عقار من عقارات <paramref name="ratedOwnerId"/> — بعكس
    /// <see cref="HasCompletedVisitAsync"/> المرتبطة بعقار واحد بعينه، هذه الدالة
    /// تفحص عبر كل عقارات المالك لأن تقييم المستخدم ليس مرتبطاً بعقار محدد.
    /// </summary>
    Task<bool> HasCompletedVisitWithOwnerAsync(
        Guid raterId,
        Guid ratedOwnerId,
        CancellationToken ct = default);
}