using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.ComponentModel;
using WohnungenApi.Models.Enums;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EnumController : ControllerBase
    {
        // Generic helper to get id/name/label for any enum type
        private object GetEnumValues<T>() where T : Enum
        {
            var values = Enum.GetValues(typeof(T))
                .Cast<T>()
                .Select(e =>
                {
                    // Get DescriptionAttribute if present
                    var memInfo = typeof(T).GetMember(e.ToString()).FirstOrDefault();
                    var descAttr = memInfo?
                        .GetCustomAttributes(typeof(DescriptionAttribute), false)
                        .FirstOrDefault() as DescriptionAttribute;
                    var label = descAttr?.Description ?? e.ToString();

                    return new { id = Convert.ToInt32(e), name = e.ToString(), label };
                })
                .ToList();

            return values;
        }

        [HttpGet("zustand")]
        public IActionResult GetWohnungsZustand()
        {
            return Ok(GetEnumValues<WohnungsZustand>());
        }

        [HttpGet("status")]
        public IActionResult GetWohnungsStatus()
        {
            return Ok(GetEnumValues<WohnungsStatus>());
        }

        [HttpGet("EnergieausweisTyp")]
        public IActionResult GetEnergieausweisTyp()
        {
            return Ok(GetEnumValues<EnergieausweisTyp>());
        }

        [HttpGet("Stadt")]
        public IActionResult GetStadt()
        {
            return Ok(GetEnumValues<Stadt>());
        }
    }
}
