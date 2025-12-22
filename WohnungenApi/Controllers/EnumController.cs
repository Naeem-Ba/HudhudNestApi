using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Dtos;
using WohnungenApi.Models.Enums;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/enums")]
    public class EnumController : ControllerBase
    {
        private static readonly Dictionary<string, Type> EnumMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "STADT", typeof(Stadt) },
                { "ZUSTAND", typeof(WohnungsZustand) },
                { "STATUS", typeof(WohnungsStatus) },
                { "ENERGIEAUSWEISTYP", typeof(EnergieausweisTyp) }
            };

        [HttpGet("{name}")]
        public IActionResult Get(string name)
        {
            if (!EnumMap.TryGetValue(name, out var enumType))
                return BadRequest($"Enum '{name}' not found.");

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
    }
}
