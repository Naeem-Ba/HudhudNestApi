using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudhudNestApi.Domain.Listings.Enums;

/// <summary>
/// حالة التنظيم العمراني للعقار.
/// WithinZoning = مُنظَّم ضمن المخطط التنظيمي الرسمي.
/// </summary>
public enum ZoningStatusType
{
    WithinZoning,   // ضمن التنظيم
    OutsideZoning,  // خارج التنظيم (أكثر شيوعاً في المناطق الريفية)
    Unknown

}


