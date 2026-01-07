using Microsoft.AspNetCore.Mvc;
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
        [RequestSizeLimit(10_000_000)] // 10 MB
        public async Task<IActionResult> UploadBilder(
            int wohnungId,
            [FromForm] IFormFileCollection files)
        {
            var wohnung = await _context.Wohnungen.FindAsync(wohnungId);
            if (wohnung == null)
                return NotFound("Wohnung nicht gefunden");

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
                    IsMain = false
                });
            }

            _context.Wohnungsbilder.AddRange(bilder);
            await _context.SaveChangesAsync();

            return Ok(bilder);
        }

        [HttpGet("{wohnungId}")]
        public IActionResult GetBilder(int wohnungId)
        {
            var bilder = _context.Wohnungsbilder.Where(b => b.WohnungId == wohnungId).ToList();
            return Ok(bilder);
        }
    }
}