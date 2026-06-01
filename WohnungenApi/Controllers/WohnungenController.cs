using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WohnungenApi.Dtos;
using WohnungenApi.Services;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WohnungenController : ControllerBase
    {
        private readonly IWohnungService _wohnungService;

        public WohnungenController(IWohnungService wohnungService)
        {
            _wohnungService = wohnungService;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateWohnung([FromForm] WohnungCreateDto dto)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var wohnung = await _wohnungService.CreateWohnungAsync(dto, userId);
            return Ok(wohnung);
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok(await _wohnungService.GetAllAsync());
        }

        [HttpGet("mine")]
        [Authorize]
        public async Task<IActionResult> GetMine()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            return Ok(await _wohnungService.GetMineAsync(userId));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var wohnung = await _wohnungService.GetByIdAsync(id);
            if (wohnung == null) return NotFound();

            return Ok(wohnung);
        }

        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> Update(int id, [FromForm] WohnungUpdateDto dto)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            try
            {
                var wohnung = await _wohnungService
                    .UpdateWohnungAsync(id, dto, userId);

                if (wohnung == null)
                    return NotFound();

                return Ok(wohnung);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [HttpDelete("{id:int}")]
        [Authorize]
        public async Task<IActionResult> Delete(int id)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            try
            {
                var deleted = await _wohnungService
                    .DeleteWohnungAsync(id, userId);

                if (!deleted)
                    return NotFound();

                return Ok("Wohnung deleted successfully");
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }
}