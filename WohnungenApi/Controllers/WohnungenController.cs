using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Data;
using WohnungenApi.Dtos;
using WohnungenApi.Models;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WohnungenController : ControllerBase
    {
        private readonly WohnungenContext _context;
        private readonly IWebHostEnvironment _env;

        public WohnungenController(WohnungenContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpPost]
        public async Task<IActionResult> CreateWohnung([FromForm] WohnungCreateDto dto)
        {
            var wohnung = new Wohnung
            {
                Titel = dto.Titel,
                Beschreibung = dto.Beschreibung,
                Adresse = dto.Adresse,
                Stadt = dto.Stadt,
                PLZ = dto.PLZ,
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
                //Energieausweis = dto.Energieausweis,
                //Zustand = dto.Zustand,
                //Status = dto.Status,
                OwnerId = dto.OwnerId,
                Bilder = new List<Wohnungsbild>()
            };

            if (dto.Bilder != null)
            {
                foreach (var file in dto.Bilder)
                {
                    var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
                    var path = Path.Combine(_env.WebRootPath, "bilder");

                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);

                    var filePath = Path.Combine(path, fileName);

                    using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream);

                    wohnung.Bilder.Add(new Wohnungsbild
                    {
                        Url = $"{Request.Scheme}://{Request.Host}/bilder/{fileName}",
                        IsMain = false
                    });
                }
            }

            _context.Wohnungen.Add(wohnung);
            await _context.SaveChangesAsync();

            return Ok(wohnung);
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Wohnung>>> Get()
        {
            var wohnungen = await _context.Wohnungen
                .Include(w => w.Bilder)
                .ToListAsync();
            return Ok(wohnungen);
        }

        [HttpGet("{id}")]
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
