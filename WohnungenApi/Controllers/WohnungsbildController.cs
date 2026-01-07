using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;
using WohnungenApi.Services; // إضافة namespace الخاص بالخدمة

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WohnungsbildController : ControllerBase
    {
        private readonly WohnungenContext _context;
        private readonly IPhotoService _photoService; // استبدال IWebHostEnvironment بالخدمة الجديدة

        public WohnungsbildController(WohnungenContext context, IPhotoService photoService)
        {
            _context = context;
            _photoService = photoService;
        }

        [HttpPost("{wohnungId}")]
        [RequestSizeLimit(10_000_000)] // 10 MB
        public async Task<IActionResult> UploadBild(int wohnungId, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                return BadRequest("Keine Datei hochgeladen.");

            var wohnung = await _context.Wohnungen.FindAsync(wohnungId);
            if (wohnung == null)
                return NotFound("Wohnung nicht gefunden.");

            // رفع الصورة إلى Cloudinary بدلاً من FileStream المحلي
            var result = await _photoService.AddPhotoAsync(file);

            if (result.Error != null)
                return BadRequest(result.Error.Message);

            // الحصول على الرابط المؤمن (https) من Cloudinary
            var url = result.SecureUrl?.AbsoluteUri?? "";

            var bild = new Wohnungsbild
            {
                WohnungId = wohnungId,
                Url = url, // الرابط السحابي الآن للأبد
                IsMain = false
            };

            _context.Wohnungsbilder.Add(bild);
            await _context.SaveChangesAsync();

            return Ok(bild);

        }
             catch (Exception ex)
             {
                      // سيخبرك هنا إذا كان الخطأ من قاعدة البيانات (مثلاً حقل ناقص)
                           return StatusCode(500, $"Database Error: {ex.Message} -> {ex.InnerException?.Message}");
               }

            }

        [HttpGet("{wohnungId}")]
        public IActionResult GetBilder(int wohnungId)
        {
            var bilder = _context.Wohnungsbilder.Where(b => b.WohnungId == wohnungId).ToList();
            return Ok(bilder);
        }
    }
}