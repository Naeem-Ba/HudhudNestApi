using Microsoft.AspNetCore.Mvc;
using PropertyApi.Domain.Enums;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Controllers;

/// <summary>
/// يُعيد قيم الـ Enums كـ JSON للـ Frontend.
///   - يستخدم Domain Enums (PropertyStatus, PropertyCondition, etc.)
///   - يُضيف HeatingType و ListingType الجديدَين
///   - يُبقي أسماء المفاتيح القديمة كـ aliases للتوافق مع الـ Frontend
/// </summary>
[ApiController]
[Route("api/enums")]
public sealed class EnumController : ControllerBase
{
    // ------------------------------------------------------------------
    // الخريطة: اسم يُرسله الـ Frontend → نوع الـ Enum المقابل في Domain
    //
    // BACKWARD COMPATIBILITY:
    //   الأسماء القديمة (STADT, ZUSTAND, STATUS, ENERGIEAUSWEISTYP) محتفظ بها
    //   لأن الـ Frontend يستخدمها. الأسماء الجديدة أُضيفت إضافةً.
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, Type> EnumMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── أسماء جديدة (Domain Enums) ──────────────────────────
            { "ListingType",        typeof(ListingType)           },
            { "PropertyStatus",     typeof(PropertyStatus)        },
            { "PropertyCondition",  typeof(PropertyCondition)     },
            { "EnergyEfficiency",   typeof(EnergyEfficiencyType)  },
            { "HeatingType",        typeof(HeatingType)           },

            // ── أسماء قديمة (legacy aliases - للتوافق مع الـ Frontend) ──
            // Frontend يُرسل "STATUS" → يحصل على PropertyStatus values
            { "STATUS",             typeof(PropertyStatus)        },
            { "ZUSTAND",            typeof(PropertyCondition)     },
            { "ENERGIEAUSWEISTYP",  typeof(EnergyEfficiencyType)  },
            // ملاحظة: "STADT" حُذف - لا يوجد مقابل في Domain الجديد
            // يمكن إضافة CountryCodes endpoint مستقل لاحقاً
        };

    // ------------------------------------------------------------------
    // GET /api/enums/{name}
    // مثال: GET /api/enums/PropertyStatus
    //        GET /api/enums/STATUS  (legacy alias)
    // ------------------------------------------------------------------
    [HttpGet("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Get(string name)
    {
        if (!EnumMap.TryGetValue(name, out var enumType))
            return BadRequest(new
            {
                message = $"Enum '{name}' not found.",
                available = EnumMap.Keys.OrderBy(k => k).ToArray()
            });

        var result = Enum.GetValues(enumType)
            .Cast<Enum>()
            .Select(e => new EnumDto
            {
                Id = Convert.ToInt32(e),
                Key = e.ToString()
            })
            .ToList();

        return Ok(result);
    }

    // ------------------------------------------------------------------
    // GET /api/enums  (اختياري: يُعيد كل الـ Enums المتاحة)
    // ------------------------------------------------------------------
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAll()
    {
        var result = EnumMap
            .GroupBy(kv => kv.Value.Name)   // تجميع الـ aliases معاً
            .Select(g => new
            {
                typeName = g.Key,
                aliases = g.Select(kv => kv.Key).OrderBy(k => k).ToArray(),
                values = Enum.GetValues(g.First().Value)
                               .Cast<Enum>()
                               .Select(e => new EnumDto
                               {
                                   Id = Convert.ToInt32(e),
                                   Key = e.ToString()
                               })
                               .ToArray()
            })
            .OrderBy(x => x.typeName)
            .ToList();

        return Ok(result);
    }
}