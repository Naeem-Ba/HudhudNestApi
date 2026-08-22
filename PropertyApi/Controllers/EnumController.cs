using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Controllers;

/// <summary>
/// يعيد قيم الـ Enums كـ JSON للـ Frontend.
/// - يستخدم Domain Enums مثل PropertyStatus و PropertyCondition.
/// - يدعم الأسماء الجديدة.
/// - يحافظ على legacy aliases مثل STATUS و ZUSTAND و ENERGIEAUSWEISTYP للتوافق مع الـ Frontend القديم.
/// </summary>
[ApiController]
[Route("api/enums")]
public sealed class EnumController : ControllerBase
{
    private static readonly Dictionary<string, Type> EnumMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // New Domain enum names
            { "ListingType", typeof(ListingType) },
            { "PropertyStatus", typeof(PropertyStatus) },
            { "PropertyCondition", typeof(PropertyCondition) },
            { "EnergyEfficiency", typeof(EnergyEfficiencyType) },
            { "HeatingType", typeof(HeatingType) },

            // Legacy aliases for frontend compatibility
            { "STATUS", typeof(PropertyStatus) },
            { "ZUSTAND", typeof(PropertyCondition) },
            { "ENERGIEAUSWEISTYP", typeof(EnergyEfficiencyType) }
        };

    /// <summary>
    /// GET /api/enums/{name}
    /// Examples:
    /// GET /api/enums/PropertyStatus
    /// GET /api/enums/STATUS
    /// </summary>
    [HttpGet("{name}")]
    [AllowAnonymous] // Public enum metadata used to render forms.
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Get(string name)
    {
        if (!EnumMap.TryGetValue(name, out var enumType))
        {
            return BadRequest(new
            {
                message = $"Enum '{name}' not found.",
                available = EnumMap.Keys.OrderBy(key => key).ToArray()
            });
        }

        var result = Enum.GetValues(enumType)
            .Cast<Enum>()
            .Select(value => new EnumDto
            {
                Id = Convert.ToInt32(value),
                Key = value.ToString()
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// GET /api/enums
    /// Returns all supported enum groups.
    /// </summary>
    [HttpGet]
    [AllowAnonymous] // Public enum metadata used to render forms.
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAll()
    {
        var result = EnumMap
            .GroupBy(item => item.Value.Name)
            .Select(group => new
            {
                typeName = group.Key,
                aliases = group
                    .Select(item => item.Key)
                    .OrderBy(key => key)
                    .ToArray(),
                values = Enum.GetValues(group.First().Value)
                    .Cast<Enum>()
                    .Select(value => new EnumDto
                    {
                        Id = Convert.ToInt32(value),
                        Key = value.ToString()
                    })
                    .ToArray()
            })
            .OrderBy(item => item.typeName)
            .ToList();

        return Ok(result);
    }
}