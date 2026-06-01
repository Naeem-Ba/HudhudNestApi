using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WohnungenApi.Data;
using WohnungenApi.Models;
using WohnungenApi.Services; // إضافة namespace الخاص بالخدمة

/////////////////////إضافة صور لاحقًا///////////////////

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/wohnungen/{wohnungId:int}/bilder")]
    public class WohnungsbildController : ControllerBase
    {
        private readonly WohnungenContext _context;
        private readonly IPhotoService _photoService; // استبدال IWebHostEnvironment بالخدمة الجديدة

        public WohnungsbildController(
            WohnungenContext context,
            IPhotoService photoService)
        {
            _context = context;
            _photoService = photoService;
        }

        [HttpPost]
        [Authorize]
        [RequestSizeLimit(10_000_000)] // 10 MB
        public async Task<IActionResult> UploadBilder(
            int wohnungId,
            [FromForm] IFormFileCollection files)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files uploaded");

            var wohnung = await _context.Wohnungen
                .FirstOrDefaultAsync(w => w.Id == wohnungId);
            if (wohnung == null)
                return NotFound("Apartment not found");

            //تحقق من الملكية ✅ 
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            if (wohnung.OwnerId != userId)
                return Forbid("You do not own this Apartment");

            var bilder = new List<Wohnungsbild>();

            foreach (var file in files)
            {
                if (file.Length == 0) continue;

                var result = await _photoService.AddPhotoAsync(file);

                if (result.Error != null)
                    return BadRequest(result.Error.Message);

                bilder.Add(new Wohnungsbild
                {
                    WohnungId = wohnungId,
                    Url = result.SecureUrl.AbsoluteUri,
                    PublicId = result.PublicId,
                    IsMain = false
                });
            }

            _context.Wohnungsbilder.AddRange(bilder);
            await _context.SaveChangesAsync();

            return Ok(bilder);
        }

        [HttpGet]
        public async Task<IActionResult> GetBilder(int wohnungId)
        {
            var bilder = await _context.Wohnungsbilder
                .Where(b => b.WohnungId == wohnungId)
                .ToListAsync();

            return Ok(bilder);
        }
    }
}