using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Domain.Listings.Enums;

/// <summary>
/// الوضع القانوني للملكية — خاص بالسوق السوري.
/// هذه القيم تعكس الواقع القانوني على الأرض في سوريا.
/// </summary>
public enum LegalStatusType
{
    /// <summary>طابو أخضر — الملكية الأقوى والأكثر تداولاً</summary>
    GreenDeed,

    /// <summary>سكن شبابي — عقارات مقسمة بأسهم، قيود على البيع</summary>
    YouthHousing,

    /// <summary>أسهم عقارية — ملكية جزئية بنسب مئوية</summary>
    Shares,

    /// <summary>كاتب عدل — عقد عادي دون تسجيل رسمي</summary>
    Notary,

    /// <summary>حكم محكمة — ملكية مثبتة قضائياً</summary>
    CourtJudgment,

    /// <summary>وثيقة إرث — ميراث لم يُسجَّل بعد</summary>
    InheritanceDocument,

    /// <summary>رهن / أمانة — عقار مرهون لصالح طرف ثالث</summary>
    Pledge,

    /// <summary>غير معروف — عقارات المناطق النائية أو ما بعد النزاعات</summary>
    Unknown

}

