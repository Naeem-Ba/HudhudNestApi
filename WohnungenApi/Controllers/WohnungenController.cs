using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Data;
using WohnungenApi.Dtos;
using WohnungenApi.Models;
using WohnungenApi.Services;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WohnungenController : ControllerBase
    {
        private readonly WohnungenContext _context;
        private readonly IPhotoService _photoService;

        public WohnungenController(
            WohnungenContext context,
            IPhotoService photoService)
        {
            _context = context;
            _photoService = photoService;
        }


        // ============================================
        // CREATE WOHNUNG + IMAGES
        // ============================================
        [HttpPost]
        public async Task<IActionResult> CreateWohnung(
            [FromForm] WohnungCreateDto dto)
        {
            // 1️ بناء كيان الشقة
            var wohnung = new Wohnung
            {
                Titel = dto.Titel,
                Beschreibung = dto.Beschreibung,
                Adresse = dto.Adresse,
                Stadt = dto.Stadt,
                PLZ = dto.PLZ,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                ZumMieten = dto.ZumMieten,
                ZumKaufen = dto.ZumKaufen,
                Kaltmiete = dto.Kaltmiete,
                Warmmiete = dto.Warmmiete,
                Kaufpreis = dto.Kaufpreis,
                Nebenkosten = dto.Nebenkosten,
                Kaution = dto.Kaution,
                Zimmer = dto.Zimmer,
                Flaeche = dto.Flaeche,
                Geschoss = dto.Geschoss,
                FreiAb = dto.FreiAb,
                Balkon = dto.Balkon,
                Aufzug = dto.Aufzug,
                Stellplatz = dto.Stellplatz,
                Heizung = dto.Heizung,
                Energieausweis = dto.Energieausweis,
                Zustand = dto.Zustand,
                Status = dto.Status,
                OwnerId = dto.OwnerId,
                Bilder = new List<Wohnungsbild>()
            };

            // 2️ رفع الصور إن وُجدت
            if (dto.Bilder != null && dto.Bilder.Count > 0)
            {
                foreach (var file in dto.Bilder)
                {
                    if (file.Length == 0) continue;
                    var result = await _photoService.AddPhotoAsync(file);

                    // التحقق من نجاح الرفع قبل الإضافة للقاعدة
                    if (result.Error == null && result.SecureUrl != null)
                    {
                        wohnung.Bilder.Add(new Wohnungsbild
                        {
                            Url = result.SecureUrl.AbsoluteUri,
                            IsMain = (wohnung.Bilder.Count == 0) // أول صورة تصبح الأساسية تلقائياً
                        });
                    }
                    else
                    {
                        // سجل الخطأ هنا لتعرف لماذا فشلت الصورة
                        Console.WriteLine($"Photo Upload Failed: {result.Error?.Message}");
                        return BadRequest($"Cloudinary Error: {result.Error.Message}");
                    }
                }
            }

            // 3️ حفظ نهائي
            _context.Wohnungen.Add(wohnung);
            await _context.SaveChangesAsync(); // الحفظ النهائي للشقة مع صورها

            return Ok(wohnung);
        }

        // ============================================
        // GET ALL
        // ============================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Wohnung>>> Get()
        {
            return await _context.Wohnungen
                .Include(w => w.Bilder)
                .ToListAsync();
        }

        // ============================================
        // GET BY ID
        // ============================================
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Wohnung>> GetById(int id)
        {
            var wohnung = await _context.Wohnungen
                .Include(w => w.Bilder)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (wohnung == null) return NotFound();
            return wohnung;
        }
    }
}