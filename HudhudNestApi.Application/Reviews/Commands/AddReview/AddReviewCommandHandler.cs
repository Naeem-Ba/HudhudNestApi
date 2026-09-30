// ═══════════════════════════════════════════════════════════════
// ✅ إصلاح M-1 (الجزء 3 من 3): إضافة فحص الزيارة في AddReviewCommandHandler
//
// المشكلة القديمة:
//   الـ Handler كان يتحقق من: عقار موجود + ليس المالك + لم يُقيِّم مسبقاً.
//   لكنه لم يتحقق من: هل المستخدم زار العقار فعلاً؟
//
// الإصلاح:
//   إضافة IVisitRepository كـ dependency جديدة.
//   استدعاء HasCompletedVisitAsync قبل السماح بإنشاء التقييم.
//
// مبدأ Fail Fast:
//   نتحقق من الشروط بالترتيب الأكثر شيوعاً → الأقل شيوعاً.
//   التحقق من الزيارة يأتي بعد التحقق من وجود العقار لأننا نحتاج
//   الـ property object للتحقق من OwnerId أولاً.
// ═══════════════════════════════════════════════════════════════
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Reviews.DTOs;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Bookings.Interfaces;  // ✅ جديد
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Reviews.Entities;

namespace HudhudNestApi.Application.Reviews.Commands.AddReview;

public sealed class AddReviewCommandHandler
    : IRequestHandler<AddReviewCommand, PropertyReviewDto>
{
    private readonly IPropertyReviewRepository _reviews;
    private readonly IPropertyReadRepository _properties;
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;
    private readonly IVisitRepository _visits;     // ✅ جديد
    private readonly ILogger<AddReviewCommandHandler> _logger;

    public AddReviewCommandHandler(
        IPropertyReviewRepository reviews,
        IPropertyReadRepository properties,
        IUnitOfWork uow,
        INotificationService notifications,
        IVisitRepository visits,               // ✅ جديد — حقن عبر DI
        ILogger<AddReviewCommandHandler> logger)
    {
        _reviews = reviews;
        _properties = properties;
        _uow = uow;
        _notifications = notifications;
        _visits = visits;              // ✅ جديد
        _logger = logger;
    }

    public async Task<PropertyReviewDto> Handle(
        AddReviewCommand request, CancellationToken ct)
    {
        // ── الفحص 1: العقار موجود ─────────────────────────────────────────
        var property = await _properties.GetByIdAsync(request.PropertyId, ct)
            ?? throw new NotFoundException("العقار غير موجود.");

        // ── الفحص 2: المُقيِّم ليس المالك ───────────────────────────────────
        // لا يُعقل أن يُقيِّم المالك عقاره الخاص
        if (property.OwnerId == request.ReviewerId)
            throw new DomainException("لا يمكن للمالك تقييم عقاره.");

        // ── الفحص 3: لم يُقيِّم مسبقاً ──────────────────────────────────────
        // الـ UNIQUE constraint في DB هو خط الدفاع الثاني، لكن نتحقق هنا أولاً
        // لإعطاء رسالة خطأ واضحة بدلاً من Database Exception
        var alreadyReviewed = await _reviews.HasUserReviewedAsync(
            request.PropertyId, request.ReviewerId, ct);
        if (alreadyReviewed)
            throw new DomainException("لقد قمت بتقييم هذا العقار مسبقاً.");

        // ── الفحص 4 ✅ جديد: التحقق من زيارة مكتملة ─────────────────────
        // التقييم يجب أن يكون مبنياً على تجربة حقيقية.
        // الزيارة يجب أن تكون بحالة Completed (ليس Pending أو Confirmed).
        //
        // لماذا يأتي هذا الفحص بعد الفحوصات السابقة؟
        //   تطبيق مبدأ Fail Fast: نتحقق من الشروط الأسرع في الفشل أولاً.
        //   فحص العقار والمالك أسرع (من ReadRepository) من فحص الزيارات (Join أكبر).
        var hasCompletedVisit = await _visits.HasCompletedVisitAsync(
            request.PropertyId, request.ReviewerId, ct);

        if (!hasCompletedVisit)
            throw new DomainException(
                "يمكن كتابة تقييم فقط بعد إتمام زيارة فعلية للعقار. " +
                "تأكد من اكتمال زيارتك أولاً.");

        // ── إنشاء التقييم في Domain Layer ────────────────────────────────
        // Factory method في Domain Entity يتحقق من قواعد الأعمال (Rating 1-5)
        var review = PropertyReview.Create(
            request.PropertyId, request.ReviewerId,
            request.Rating, request.Comment);

        await _reviews.AddAsync(review, ct);
        await _uow.SaveChangesAsync(ct);

        // ── إرسال إشعار لمالك العقار ─────────────────────────────────────
        // ✅ إصلاح: غير حرج — التقييم محفوظ بالفعل بالسطر أعلاه. لُفّ بـ
        // try/catch (نفس نمط SendMessageCommandHandler) بعد أن تبيّن أن هذا
        // الاستدعاء كان بدون حماية ويُسقط POST /api/Reviews بأكمله عند أي
        // فشل بالإشعار (نفس فئة الخلل الذي أُصلح في RequestVisitCommandHandler).
        try
        {
            await _notifications.NotifyPropertyUpdateAsync(
                recipientId: property.OwnerId,
                propertyId: property.Id,
                propertyTitle: property.Title,
                type: NotificationType.ReviewAdded,
                detail: $"تم إضافة تقييم جديد ({request.Rating}/5) لعقارك.",
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create/send review-added notification. " +
                "ReviewId={ReviewId}, PropertyId={PropertyId}, ReviewerId={ReviewerId}",
                review.Id,
                property.Id,
                request.ReviewerId);
        }

        return new PropertyReviewDto(
            Id: review.Id,
            PropertyId: review.PropertyId,
            ReviewerId: review.ReviewerId,
            ReviewerName: "—",   // resolved in query handler with navigation
            ReviewerImageUrl: null,
            Rating: review.Rating,
            Comment: review.Comment,
            CreatedAt: review.CreatedAt);
    }
}