namespace PropertyApi.Domain.Listings.Enums;

/// <summary>
/// مدة عقد الإيجار المعروضة في الإعلان. الإيجار في المرحلة الحالية محدَّد المدة
/// دائماً — يجب أن يترافق مع Property.RentalStartDate و Property.RentalEndDate.
///
/// SixMonths و OneYear خياران جاهزان تُحسب نهايتهما تلقائياً في الواجهة من
/// تاريخ البداية (تسهيل للمستخدم فقط)؛ الـ Backend لا يفرض أن الفرق بينهما يطابق
/// المدة تماماً — كل ما يتحقق منه هو RentalEndDate > RentalStartDate.
/// Custom يترك التاريخين قابلين للتعديل اليدوي الكامل.
/// </summary>
public enum RentalDurationType
{
    SixMonths,
    OneYear,
    Custom
}
