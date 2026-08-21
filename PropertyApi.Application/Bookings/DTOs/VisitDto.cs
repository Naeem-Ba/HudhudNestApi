using PropertyApi.Domain.Bookings.Enums;

namespace PropertyApi.Application.Bookings.DTOs;

// ✅ إصلاح: أُضيف OwnerId — كان غائبًا تمامًا عن هذا الـ DTO رغم أن الواجهة
// الأمامية (visits.page.ts: canManageAsOwner/canComplete) تعتمد على
// visit.ownerId === currentUserId لإظهار أزرار "قبول/رفض/إغلاق" لمالك
// العقار. بدون هذا الحقل تصل القيمة undefined دائمًا فتُقارَن خطأً بمعرّف
// المستخدم ولا تظهر الأزرار أبدًا حتى لو وصل الطلب لصندوق المالك أصلاً.
public sealed record VisitDto(
    Guid Id,
    Guid PropertyId,
    string PropertyTitle,
    string PropertyCity,
    string? PropertyMainImageUrl,
    Guid RequesterId,
    string RequesterName,
    string VisitorName,
    string VisitorPhone,
    string? VisitorNote,
    DateTime ProposedAt,
    string? OwnerNote,
    DateTime? RespondedAt,
    VisitStatus Status,
    DateTime CreatedAt,
    Guid OwnerId
);

